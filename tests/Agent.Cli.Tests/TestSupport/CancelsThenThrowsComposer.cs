using Agent.Composition;
using Agent.Domain;

namespace Agent.Cli.Tests.TestSupport;

// Cancels the run from inside compose and then fails the record with a fault that is not a
// cancellation, so the record folds as a failed record without anything in the batch having
// observed the token. What stops the next record from starting is then the batch loop's own
// check before each start.
internal sealed class CancelsThenThrowsComposer(CancellationTokenSource runCancellation) : IMessageComposer
{
    public Task<ComposeOutcome> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<DateTimeOffset>? tourSlots = null)
    {
        runCancellation.Cancel();
        throw new InvalidOperationException($"Injected fault for '{prospectCase.TaskId}'.");
    }
}
