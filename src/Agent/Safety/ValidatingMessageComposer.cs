using Agent.Common;
using Agent.Composition;
using Agent.Domain;
using Microsoft.Extensions.Logging;

namespace Agent.Safety;

// Bounded compose-validate loop: one retry through the inner composer, then a hard
// stop at the fallback composer. Never loops unboundedly (BACKLOG 4.2). The fallback's
// output is validated too: "nothing unsafe leaves the agent" applies to every exit path,
// not just the retried ones, so an unsafe fallback yields Result.Failure rather than
// shipping unvalidated content.
public sealed class ValidatingMessageComposer(
    IMessageComposer innerComposer,
    ISafetyValidator validator,
    IMessageComposer fallbackComposer,
    ILogger<ValidatingMessageComposer>? logger = null) : IMessageComposer
{
    private const int MaxComposeAttempts = 2;

    private readonly ILogger<ValidatingMessageComposer> log = logger.OrNullLogger();

    public async Task<Result<ComposedMessage>> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string>? violationsForNextAttempt = priorViolations;

        // D28 addendum: a retry a discarded attempt spent is still a retry this record
        // spent. A Result.Failure attempt carries no ComposedMessage at all (the composer
        // never built one), so only a validation-rejected attempt's retries are capturable
        // here; that is the discard case D28's own visibility goal is about.
        int discardedNetworkRetries = 0;

        for (int attempt = 1; attempt <= MaxComposeAttempts; attempt++)
        {
            Result<ComposedMessage> attemptResult = await innerComposer.ComposeAsync(prospectCase, channel, violationsForNextAttempt, cancellationToken);

            if (attemptResult.IsSuccess)
            {
                SafetyValidationResult validation = validator.Validate(attemptResult.Value.Message, prospectCase.ConstraintsOrEmpty);

                if (validation.Violations.Count == 0)
                {
                    return WithAttempts(attemptResult.Value, attempt, discardedNetworkRetries);
                }

                log.LogWarning(
                    "Compose attempt {Attempt} failed safety validation: {Violations}.",
                    attempt,
                    string.Join("; ", validation.Violations));
                violationsForNextAttempt = validation.Violations;
                discardedNetworkRetries += attemptResult.Value.Notes.NetworkRetries ?? 0;
            }
            else
            {
                // Not a safety violation, but still something the next attempt should
                // know about - otherwise a retry after a Result.Failure (a wrong cta_type,
                // a malformed completion) repeats the exact same prompt with no corrective
                // signal, wasting the one retry this loop has.
                // The error text can carry raw model response content on the OpenAI path,
                // so the log records the failure category and never the content. The text
                // itself still reaches the next attempt as a correction below; it just
                // never reaches a log sink.
                log.LogWarning("Compose attempt {Attempt} failed: the composer returned a failure result.", attempt);
                violationsForNextAttempt = [attemptResult.Error];
            }
        }

        log.LogWarning("Both compose attempts were rejected; falling back to the safe fallback composer.");
        Result<ComposedMessage> fallbackResult = await fallbackComposer.ComposeAsync(prospectCase, channel, cancellationToken: cancellationToken);

        if (fallbackResult.IsSuccess &&
            validator.Validate(fallbackResult.Value.Message, prospectCase.ConstraintsOrEmpty).Violations.Count == 0)
        {
            return WithAttempts(fallbackResult.Value, MaxComposeAttempts + 1, discardedNetworkRetries);
        }

        log.LogError("Fallback composer output also failed composition or safety validation; suppressing.");
        return Result<ComposedMessage>.Failure("Fallback composer output failed safety validation.");
    }

    // D24: the composer that answered keeps its own name, and this loop supplies the count,
    // because the number of calls it took is the loop's fact and not the composer's.
    // discardedNetworkRetries carries retries spent on attempts this loop rejected: added
    // into the winning attempt's own count rather than lost with the attempt that made them.
    private static Result<ComposedMessage> WithAttempts(ComposedMessage composed, int attempts, int discardedNetworkRetries)
    {
        int? networkRetries = composed.Notes.NetworkRetries switch
        {
            null when discardedNetworkRetries == 0 => null,
            null => discardedNetworkRetries,
            int current => current + discardedNetworkRetries,
        };

        return Result<ComposedMessage>.Success(composed with { Notes = composed.Notes with { Attempts = attempts, NetworkRetries = networkRetries } });
    }
}
