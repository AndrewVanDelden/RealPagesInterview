using Agent.Composition;
using Agent.Domain;

namespace Agent.Cli.Tests.TestSupport;

// One record throws an OperationCanceledException the run never asked for, the way a
// component's own timeout does, once every other record is composing. Every other record waits
// on its token, bounded by WaitLimit so a run that never cancels it still ends, and on
// cancellation holds for HoldAfterCancellation before it lets go, so a run that did not await
// it would have thrown before it let go.
internal sealed class UnrequestedCancellationComposer(string taskIdThatThrows) : IMessageComposer
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HoldAfterCancellation = TimeSpan.FromMilliseconds(200);

    private readonly TaskCompletionSource otherRecordComposing = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int releasedOnCancellation;

    public bool OtherRecordReleasedOnCancellation => Volatile.Read(ref releasedOnCancellation) == 1;

    public async Task<ComposeOutcome> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<DateTimeOffset>? tourSlots = null)
    {
        if (prospectCase.TaskId == taskIdThatThrows)
        {
            await otherRecordComposing.Task.WaitAsync(WaitLimit, CancellationToken.None);
            throw new OperationCanceledException($"Injected cancellation for '{taskIdThatThrows}'.");
        }

        otherRecordComposing.TrySetResult();
        try
        {
            await Task.Delay(WaitLimit, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await Task.Delay(HoldAfterCancellation, CancellationToken.None);
            Volatile.Write(ref releasedOnCancellation, 1);
            throw;
        }

        throw new InvalidOperationException($"The run never cancelled '{prospectCase.TaskId}'.");
    }
}
