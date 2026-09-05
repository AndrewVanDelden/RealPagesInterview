using Agent.Common;
using Agent.Composition;
using Agent.Domain;

namespace Agent.Tests.TestSupport;

// A composer that always throws, independent of cancellation - distinguishes "genuine bug
// deep in the call graph" from ThrowsOnCancellationComposer's "cancellation requested"
// scenario, so LeasingMessageAgent's exception-logging filter can be tested against both.
internal sealed class ThrowsComposer : IMessageComposer
{
    public Task<Result<NextMessage>> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated unhandled failure deep in the compose step.");
}
