using Agent.Common;
using Agent.Composition;
using Agent.Domain;

namespace Agent.Safety;

// Bounded compose-validate loop: one retry through the inner composer, then a hard
// stop at the fallback composer. Never loops unboundedly (BACKLOG 4.2). The fallback's
// output is validated too: "nothing unsafe leaves the agent" applies to every exit path,
// not just the retried ones, so an unsafe fallback yields Result.Failure rather than
// shipping unvalidated content.
public sealed class ValidatingMessageComposer(
    IMessageComposer innerComposer,
    ISafetyValidator validator,
    IMessageComposer fallbackComposer) : IMessageComposer
{
    public async Task<Result<NextMessage>> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string>? violationsForNextAttempt = priorViolations;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            Result<NextMessage> attemptResult = await innerComposer.ComposeAsync(prospectCase, channel, violationsForNextAttempt, cancellationToken);

            if (attemptResult.IsSuccess)
            {
                SafetyValidationResult validation = validator.Validate(attemptResult.Value, prospectCase.Assertions.Constraints);

                if (validation.Violations.Count == 0)
                {
                    return attemptResult;
                }

                violationsForNextAttempt = validation.Violations;
            }
            else
            {
                // Not a safety violation, but still something the next attempt should
                // know about - otherwise a retry after a Result.Failure (a wrong cta_type,
                // a malformed completion) repeats the exact same prompt with no corrective
                // signal, wasting the one retry this loop has.
                violationsForNextAttempt = [attemptResult.Error];
            }
        }

        Result<NextMessage> fallbackResult = await fallbackComposer.ComposeAsync(prospectCase, channel, cancellationToken: cancellationToken);

        if (fallbackResult.IsSuccess &&
            validator.Validate(fallbackResult.Value, prospectCase.Assertions.Constraints).Violations.Count == 0)
        {
            return fallbackResult;
        }

        return Result<NextMessage>.Failure("Fallback composer output failed safety validation.");
    }
}
