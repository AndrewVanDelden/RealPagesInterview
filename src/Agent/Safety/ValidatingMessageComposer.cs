using Agent.Common;
using Agent.Composition;
using Agent.Domain;

namespace Agent.Safety;

// Bounded compose-validate loop: one retry through the inner composer, then a hard
// stop at the fallback composer. Never loops unboundedly (BACKLOG 4.2).
public sealed class ValidatingMessageComposer(
    IMessageComposer innerComposer,
    ISafetyValidator validator,
    IMessageComposer fallbackComposer) : IMessageComposer
{
    public async Task<Result<NextMessage>> ComposeAsync(ProspectCase prospectCase, CommunicationChannel channel, CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Result<NextMessage> attemptResult = await innerComposer.ComposeAsync(prospectCase, channel, cancellationToken);

            if (attemptResult.IsSuccess &&
                validator.Validate(attemptResult.Value, prospectCase.Assertions.Constraints).Violations.Count == 0)
            {
                return attemptResult;
            }
        }

        return await fallbackComposer.ComposeAsync(prospectCase, channel, cancellationToken);
    }
}
