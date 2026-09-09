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
        // spent. An attempt that carries no ComposedMessage at all (the composer never
        // built one) has no retry count to capture, so only a validation-rejected attempt's
        // retries are capturable here; that is the discard case D28's own visibility goal is
        // about.
        int discardedNetworkRetries = 0;

        for (int attempt = 1; attempt <= MaxComposeAttempts; attempt++)
        {
            ComposeOutcome attemptOutcome = await innerComposer.ComposeAsync(prospectCase, channel, violationsForNextAttempt, cancellationToken);

            if (attemptOutcome is ComposeOutcome.Composed attemptComposed)
            {
                SafetyValidationResult validation = validator.Validate(attemptComposed.Message.Message, prospectCase.ConstraintsOrEmpty);

                if (validation.Violations.Count == 0)
                {
                    return WithAttempts(attemptComposed.Message, attempt, discardedNetworkRetries);
                }

                log.LogWarning(
                    "Compose attempt {Attempt} failed safety validation: {Violations}.",
                    attempt,
                    string.Join("; ", validation.Violations));
                violationsForNextAttempt = validation.Violations;
                discardedNetworkRetries += attemptComposed.Message.Notes.NetworkRetries ?? 0;
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
                violationsForNextAttempt = [((ComposeOutcome.NoMessage)attemptOutcome).Error];
            }
        }

        log.LogWarning("Both compose attempts were rejected; falling back to the safe fallback composer.");
        ComposeOutcome fallbackOutcome = await fallbackComposer.ComposeAsync(prospectCase, channel, cancellationToken: cancellationToken);

        if (fallbackOutcome is not ComposeOutcome.Composed fallbackComposed)
        {
            // No draft anywhere: the fallback built no message either. Nothing to review, so
            // this stays a failure rather than becoming an empty queue row.
            log.LogError("Fallback composer produced no message; suppressing.");
            return new ComposeOutcome.Failed(((ComposeOutcome.NoMessage)fallbackOutcome).Error);
        }

        if (validator.Validate(fallbackComposed.Message.Message, prospectCase.ConstraintsOrEmpty).Violations.Count == 0)
        {
            return WithAttempts(fallbackComposed.Message, MaxComposeAttempts + 1, discardedNetworkRetries);
        }

        log.LogError("Fallback composer output also failed safety validation; refusing and carrying the draft out for review.");
        return new ComposeOutcome.Refused(fallbackComposed.Message.Message, RefusalError);
    }

    // D24: the composer that answered keeps its own name, and this loop supplies the count,
    // because the number of calls it took is the loop's fact and not the composer's.
    // discardedNetworkRetries carries retries spent on attempts this loop rejected: added
    // into the winning attempt's own count rather than lost with the attempt that made them.
    private static ComposeOutcome WithAttempts(ComposedMessage composed, int attempts, int discardedNetworkRetries)
    {
        int? networkRetries = composed.Notes.NetworkRetries switch
        {
            null when discardedNetworkRetries == 0 => null,
            null => discardedNetworkRetries,
            int current => current + discardedNetworkRetries,
        };

        return new ComposeOutcome.Composed(composed with { Notes = composed.Notes with { Attempts = attempts, NetworkRetries = networkRetries } });
    }
}
