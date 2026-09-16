using Agent.Composition;
using Agent.Domain;

namespace Agent.Cli.Tests.TestSupport;

// Cancels the run from inside compose and then fails the record with a fault that is not a
// cancellation, so the record folds as a failed record without anything in the batch having
// observed the token. What stops the batch is then the fold's own check before each line.
internal sealed class CancelsThenThrowsComposer(CancellationTokenSource runCancellation) : IMessageComposer
{
    public Task<ComposeOutcome> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<DateTimeOffset>? tourSlots = null,
        DateOnly? referenceDate = null)
    {
        runCancellation.Cancel();
        throw new InvalidOperationException($"Injected fault for '{prospectCase.TaskId}'.");
    }
}
