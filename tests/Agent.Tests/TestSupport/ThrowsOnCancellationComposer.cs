using Agent.Common;
using Agent.Composition;
using Agent.Domain;

namespace Agent.Tests.TestSupport;

internal sealed class ThrowsOnCancellationComposer : IMessageComposer
{
    public Task<Result<NextMessage>> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("Should have thrown for cancellation before reaching here.");
    }
}
