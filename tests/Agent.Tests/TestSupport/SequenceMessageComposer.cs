using Agent.Common;
using Agent.Composition;
using Agent.Domain;

namespace Agent.Tests.TestSupport;

internal sealed class SequenceMessageComposer(params Result<NextMessage>[] results) : IMessageComposer
{
    public int CallCount { get; private set; }

    public Task<Result<NextMessage>> ComposeAsync(ProspectCase prospectCase, CommunicationChannel channel, CancellationToken cancellationToken = default)
    {
        Result<NextMessage> result = results[Math.Min(CallCount, results.Length - 1)];
        CallCount++;
        return Task.FromResult(result);
    }
}
