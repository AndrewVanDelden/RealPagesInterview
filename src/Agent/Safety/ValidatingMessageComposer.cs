using Agent.Common;
using Agent.Composition;
using Agent.Domain;
using Microsoft.Extensions.Logging;

namespace Agent.Safety;

// Bounded compose-validate loop: one retry through the inner composer, then a hard
// stop at the fallback composer. Never loops unboundedly (BACKLOG 4.2). D34: only a safety
// rejection is retried, since only it has a reason to feed back into the prompt; an attempt
// that returned no message goes straight to the fallback. The fallback's
// output is validated too: "nothing unsafe leaves the agent" applies to every exit path,
// not just the retried ones, so an unsafe fallback yields ComposeOutcome.Refused rather than
// shipping unvalidated content.
//
// D48 changed what that refusal carries, not where the gate is. A refused draft used to
// become a bare error string and the draft was destroyed here, so no human could ever see
// what was rejected and the orchestrator read the record as a composition failure. It now
// leaves as Refused, with the draft; the orchestrator's own step 5 gate validates it and is
// the one place the violations are named.
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

        // D28 addendum: a retry a discarded attempt spent is still a retry this record
        // spent, whether or not that attempt built a message. D66 put NetworkRetries on the
        // ComposeOutcome base type for all three cases, so a NoMessage attempt (a wrong
        // cta_type, a malformed completion) can carry a real retry count too, and it is
        // captured here the same way a validation-rejected Composed attempt's is below.
        // Nullable, and not an int starting at zero: a record whose attempts made no network
        // call at all has no measurement, and a template-composed draft the loop refuses would
        // otherwise report a measured zero retries for calls that never happened (D28, D62).
        int? discardedNetworkRetries = null;

        // D62: this loop owns the per-record cost sum, for the reason D24's addendum gives for
        // Attempts. A composer knows what its own call cost and nothing about the calls the
        // attempts beside it made, so the total is a fact only the code that ran every attempt
        // has. It counts the attempts this loop threw away, message or no message: an attempt
        // abandoned at its timeout produced nothing to ship and the vendor billed for it anyway.
        ModelCostNotes? discardedModelCost = null;

        // D34: how many model attempts ran before the fallback, which is 1 when the first
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
                // D34: no message at all (a transport failure, a timeout, a malformed or wrong
                // completion) is not a content problem, so a second prompt has no mechanism to
                // do better and would only add a second wait. This attempt goes straight to
                // the fallback, which is deterministic and always available.
                // The error text can carry raw model response content on the OpenAI path,
                // so the log records the failure category and never the content.
                // Read as NoMessage rather than as Failed and Refused separately: an inner
                // composer that refuses its own draft is the same fact to this loop, no
                // message to ship, and no composer in this program does it, so a branch of its
                // own would be one no test could reach. What the attempt spent still rides onto
                // the outcome (D66).
                log.LogWarning("Compose attempt {Attempt} failed: the composer returned a failure result.", attempt);
                var noMessage = (ComposeOutcome.NoMessage)attemptOutcome;
                discardedNetworkRetries = AddRetries(discardedNetworkRetries, noMessage.NetworkRetries);
                discardedModelCost = ModelCostNotes.Add(discardedModelCost, noMessage.ModelCost);
                break;
            }
        }

        log.LogWarning("No compose attempt produced a clean message; falling back to the safe fallback composer.");
        ComposeOutcome fallbackOutcome = await fallbackComposer.ComposeAsync(prospectCase, channel, cancellationToken: cancellationToken);

        // D66: the two exits below return no message, so there are no notes to stamp, and
        // before D66 that is where the accumulation above was dropped. Both counts go on the
        // outcome itself instead. D67 (a): each exit also adds the fallback outcome's own
        // counts, the way WithAttempts adds the winner's, so all three exits sum both sources.
        // The template fallback this program wires spends nothing, so today that adds null.
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

    // D24: the composer that answered keeps its own name, and this loop supplies the count,
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

    // Claude Code review of PR #28: the three exits below each combined what the loop discarded
    // with one outcome's own spend the same way, written out three times. discardedNetworkRetries
    // carries retries spent on attempts this loop rejected: added into the winning attempt's or
    // the fallback's own count rather than lost with the attempt that made them.
    // discardedModelCost is the same fact for D62's token counts, and it matters most where the
    // winning outcome has no cost of its own: the template fallback answering after an abandoned
    // model call would otherwise report a record that never called a model. One helper for all
    // three exits, since ModelCost and NetworkRetries live on the shared ComposeOutcome base
    // (D66), so the combining rule cannot read differently at any of them.
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
