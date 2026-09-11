using Agent.Common;
using Agent.Composition;
using Agent.Domain;
using Microsoft.Extensions.Logging;

namespace Agent.Safety;

// Bounded compose-validate loop: one retry through the inner composer, then a hard stop at
// the fallback composer (BACKLOG 4.2). Only a safety rejection is retried, since only it has a
// reason to feed back into the prompt; an attempt that returned no message goes straight to the
// fallback. The fallback's output is validated too: nothing unsafe leaves the agent on any exit
// path. An unsafe fallback yields ComposeOutcome.Refused carrying the draft, not a bare error
// string, so a person can see what was rejected in the review queue; the orchestrator's step 5
// gate validates it and is the one place the violations are named.
public sealed class ValidatingMessageComposer(
    IMessageComposer innerComposer,
    ISafetyValidator validator,
    IMessageComposer fallbackComposer,
    ILogger<ValidatingMessageComposer>? logger = null) : IMessageComposer
{
    private const int MaxComposeAttempts = 2;

    private const string RefusalError = "Fallback composer output failed safety validation.";

    private readonly ILogger<ValidatingMessageComposer> log = logger.OrNullLogger();

    public async Task<ComposeOutcome> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string>? violationsForNextAttempt = priorViolations;

        // A retry a discarded attempt spent is still a retry this record spent, whether or not
        // that attempt built a message, so a NoMessage attempt's count (a wrong cta_type, a
        // malformed completion) is captured the same way a rejected Composed attempt's is below.
        // Nullable, and not an int starting at zero: a record whose attempts made no network
        // call at all has no measurement, and a template-composed draft the loop refuses would
        // otherwise report a measured zero retries for calls that never happened.
        int? discardedNetworkRetries = null;

        // This loop owns the per-record token cost sum, as it owns Attempts: a composer knows
        // what its own call cost and nothing about the calls the attempts beside it made, so the
        // total is a fact only the code that ran every attempt has. It counts the attempts this loop threw away, message or no message: an attempt
        // abandoned at its timeout produced nothing to ship and the vendor billed for it anyway.
        ModelCostNotes? discardedModelCost = null;

        // How many model attempts ran before the fallback, which is 1 when the first
        // attempt returned no message and MaxComposeAttempts when both were rejected on safety.
        int modelAttempts = 0;

        for (int attempt = 1; attempt <= MaxComposeAttempts; attempt++)
        {
            modelAttempts = attempt;
            ComposeOutcome attemptOutcome = await innerComposer.ComposeAsync(prospectCase, channel, violationsForNextAttempt, cancellationToken);

            if (attemptOutcome is ComposeOutcome.Composed attemptComposed)
            {
                SafetyValidationResult validation = validator.Validate(attemptComposed.Message.Message, prospectCase.ConstraintsOrEmpty);

                if (validation.Violations.Count == 0)
                {
                    return WithAttempts(
                        attemptComposed,
                        attempt,
                        discardedNetworkRetries,
                        discardedModelCost,
                        new DraftValidation(validator, attemptComposed.Message.Message, prospectCase.ConstraintsOrEmpty, validation));
                }

                log.LogWarning(
                    "Compose attempt {Attempt} failed safety validation: {Violations}.",
                    attempt,
                    string.Join("; ", validation.Violations));
                violationsForNextAttempt = validation.Violations;
                discardedNetworkRetries = AddRetries(discardedNetworkRetries, attemptComposed.NetworkRetries);
                discardedModelCost = ModelCostNotes.Add(discardedModelCost, attemptComposed.ModelCost);
            }
            else
            {
                // No message at all (a transport failure, a timeout, a malformed or wrong
                // completion) is not a content problem, so a second prompt has no mechanism to do
                // better and would only add a second wait; the deterministic fallback answers.
                // The log records the failure category, never the error text, which can carry raw
                // model response content. Failed and Refused are read as one NoMessage: an inner
                // composer refusing its own draft is the same fact here, and none in this program
                // does, so its own branch would be untestable. What it spent still rides onward.
                log.LogWarning("Compose attempt {Attempt} failed: the composer returned a failure result.", attempt);
                var noMessage = (ComposeOutcome.NoMessage)attemptOutcome;
                discardedNetworkRetries = AddRetries(discardedNetworkRetries, noMessage.NetworkRetries);
                discardedModelCost = ModelCostNotes.Add(discardedModelCost, noMessage.ModelCost);
                break;
            }
        }

        log.LogWarning("No compose attempt produced a clean message; falling back to the safe fallback composer.");
        ComposeOutcome fallbackOutcome = await fallbackComposer.ComposeAsync(prospectCase, channel, cancellationToken: cancellationToken);

        // The two exits below return no message, so there are no notes to stamp, and the spend
        // accumulated above goes on the outcome itself or it is lost. Each exit also adds the
        // fallback outcome's own counts, as WithAttempts adds the winner's, so all three exits sum
        // both sources: a fallback that spends is billed on every exit, not only the one that
        // ships. The template fallback this program wires spends nothing, so today that adds null.
        if (fallbackOutcome is not ComposeOutcome.Composed fallbackComposed)
        {
            // No draft anywhere: the fallback built no message either. Nothing to review, so
            // this stays a failure rather than becoming an empty queue row.
            log.LogError("Fallback composer produced no message; suppressing.");
            (int? networkRetries, ModelCostNotes? modelCost) = CombinedSpend(discardedNetworkRetries, discardedModelCost, fallbackOutcome);
            return new ComposeOutcome.Failed(((ComposeOutcome.NoMessage)fallbackOutcome).Error)
            {
                ModelCost = modelCost,
                NetworkRetries = networkRetries,
            };
        }

        SafetyValidationResult fallbackValidation = validator.Validate(fallbackComposed.Message.Message, prospectCase.ConstraintsOrEmpty);

        if (fallbackValidation.Violations.Count == 0)
        {
            return WithAttempts(
                fallbackComposed,
                modelAttempts + 1,
                discardedNetworkRetries,
                discardedModelCost,
                new DraftValidation(validator, fallbackComposed.Message.Message, prospectCase.ConstraintsOrEmpty, fallbackValidation));
        }

        log.LogError("Fallback composer output also failed safety validation; refusing and carrying the draft out for review.");
        (int? refusedNetworkRetries, ModelCostNotes? refusedModelCost) = CombinedSpend(discardedNetworkRetries, discardedModelCost, fallbackComposed);
        return new ComposeOutcome.Refused(fallbackComposed.Message.Message, RefusalError)
        {
            ModelCost = refusedModelCost,
            NetworkRetries = refusedNetworkRetries,
        };
    }

    // The composer that answered keeps its own name, and this loop supplies the count,
    // because the number of calls it took is the loop's fact and not the composer's.
    // The verdict that passed this message goes out with it, so the agent's final gate reads it
    // instead of asking the same validator the same question about the same draft again.
    private static ComposeOutcome WithAttempts(
        ComposeOutcome.Composed composed,
        int attempts,
        int? discardedNetworkRetries,
        ModelCostNotes? discardedModelCost,
        DraftValidation validation)
    {
        (int? networkRetries, ModelCostNotes? modelCost) = CombinedSpend(discardedNetworkRetries, discardedModelCost, composed);
        return composed with
        {
            Message = composed.Message with { Notes = composed.Message.Notes with { Attempts = attempts } },
            NetworkRetries = networkRetries,
            ModelCost = modelCost,
            Validation = validation,
        };
    }

    // One rule for combining what the loop discarded with one outcome's own spend, shared by all
    // three exits (both counts live on the ComposeOutcome base) so it cannot read differently at
    // any of them. discardedNetworkRetries is the retries spent on rejected attempts, added into
    // the winner's or the fallback's own count rather than lost with the attempt that made them.
    // discardedModelCost is the same for token counts, and matters most where the winning outcome
    // has none of its own: the template fallback answering after an abandoned model call would
    // otherwise report a record that never called a model.
    private static (int? NetworkRetries, ModelCostNotes? ModelCost) CombinedSpend(
        int? discardedNetworkRetries,
        ModelCostNotes? discardedModelCost,
        ComposeOutcome outcome) =>
        (AddRetries(discardedNetworkRetries, outcome.NetworkRetries), ModelCostNotes.Add(discardedModelCost, outcome.ModelCost));

    // The rule ModelCostNotes.Add states, for the retry count: null plus anything is that
    // thing, so an attempt that made no network call adds no measurement and a record whose
    // every attempt stayed offline still reports null rather than a zero nobody measured.
    // O(1).
    private static int? AddRetries(int? left, int? right)
    {
        if (left is null)
        {
            return right;
        }

        // Both .Value, not `left + right`: the lifted operator re-tests both for null, which is
        // a branch the two returns above have already answered and no input can reach.
        return right is null ? left : left.Value + right.Value;
    }
}
