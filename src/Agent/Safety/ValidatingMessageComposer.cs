using Agent.Common;
using Agent.Composition;
using Agent.Domain;
using Microsoft.Extensions.Logging;

namespace Agent.Safety;

// Bounded compose-validate loop: one retry through the inner composer, then a hard
// stop at the fallback composer. Never loops unboundedly (BACKLOG 4.2). The fallback's
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

        for (int attempt = 1; attempt <= MaxComposeAttempts; attempt++)
        {
            ComposeOutcome attemptOutcome = await innerComposer.ComposeAsync(prospectCase, channel, violationsForNextAttempt, cancellationToken);

            if (attemptOutcome is ComposeOutcome.Composed attemptComposed)
            {
                SafetyValidationResult validation = validator.Validate(attemptComposed.Message.Message, prospectCase.ConstraintsOrEmpty);

                if (validation.Violations.Count == 0)
                {
                    return WithAttempts(attemptComposed, attempt, discardedNetworkRetries, discardedModelCost);
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
                // Not a safety violation this loop found, but still something the next
                // attempt should know about - otherwise a retry after a failure (a wrong
                // cta_type, a malformed completion) repeats the exact same prompt with no
                // corrective signal, wasting the one retry this loop has.
                // The error text can carry raw model response content on the OpenAI path,
                // so the log records the failure category and never the content. The text
                // itself still reaches the next attempt as a correction below; it just
                // never reaches a log sink.
                // Read as NoMessage rather than as Failed and Refused separately: an inner
                // composer that refuses its own draft is the same fact to this loop, no
                // message to ship and a reason for the next attempt, and no composer in this
                // program does it, so a branch of its own would be one no test could reach.
                log.LogWarning("Compose attempt {Attempt} failed: the composer returned a failure result.", attempt);
                var noMessage = (ComposeOutcome.NoMessage)attemptOutcome;
                violationsForNextAttempt = [noMessage.Error];
                discardedNetworkRetries = AddRetries(discardedNetworkRetries, noMessage.NetworkRetries);
                discardedModelCost = ModelCostNotes.Add(discardedModelCost, noMessage.ModelCost);
            }
        }

        log.LogWarning("Both compose attempts were rejected; falling back to the safe fallback composer.");
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
            return new ComposeOutcome.Failed(((ComposeOutcome.NoMessage)fallbackOutcome).Error)
            {
                ModelCost = ModelCostNotes.Add(discardedModelCost, fallbackOutcome.ModelCost),
                NetworkRetries = AddRetries(discardedNetworkRetries, fallbackOutcome.NetworkRetries),
            };
        }

        if (validator.Validate(fallbackComposed.Message.Message, prospectCase.ConstraintsOrEmpty).Violations.Count == 0)
        {
            return WithAttempts(fallbackComposed, MaxComposeAttempts + 1, discardedNetworkRetries, discardedModelCost);
        }

        log.LogError("Fallback composer output also failed safety validation; refusing and carrying the draft out for review.");
        return new ComposeOutcome.Refused(fallbackComposed.Message.Message, RefusalError)
        {
            ModelCost = ModelCostNotes.Add(discardedModelCost, fallbackComposed.ModelCost),
            NetworkRetries = AddRetries(discardedNetworkRetries, fallbackComposed.NetworkRetries),
        };
    }

    // D24: the composer that answered keeps its own name, and this loop supplies the count,
    // because the number of calls it took is the loop's fact and not the composer's.
    // discardedNetworkRetries carries retries spent on attempts this loop rejected: added
    // into the winning attempt's own count rather than lost with the attempt that made them.
    // discardedModelCost is the same fact for D62's token counts, and it matters most where the
    // winning attempt has no cost of its own: the template fallback answering after two
    // abandoned model calls would otherwise report a record that never called a model.
    private static ComposeOutcome WithAttempts(
        ComposeOutcome.Composed composed,
        int attempts,
        int? discardedNetworkRetries,
        ModelCostNotes? discardedModelCost)
    {
        return composed with
        {
            Message = composed.Message with { Notes = composed.Message.Notes with { Attempts = attempts } },
            NetworkRetries = AddRetries(discardedNetworkRetries, composed.NetworkRetries),
            ModelCost = ModelCostNotes.Add(discardedModelCost, composed.ModelCost),
        };
    }

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
