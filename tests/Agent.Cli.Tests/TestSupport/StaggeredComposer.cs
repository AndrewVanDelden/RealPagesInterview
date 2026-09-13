using Agent.Composition;
using Agent.Domain;

namespace Agent.Cli.Tests.TestSupport;

// Makes a batch's records overlap and finish out of input order, so a test can tell a
// concurrent batch loop from a sequential one, and an input-order fold from a completion-order
// one. Every compose waits until every record in taskIdsInInputOrder has started composing,
// which a sequential loop never reaches (the wait gives up after BarrierLimit and that record
// fails), then holds for longer the earlier its record is in the input, so the last record
// finishes first. It then composes through the real template composer, or, with
// throwInjectedFault, throws a fault that names its record.
internal sealed class StaggeredComposer(string[] taskIdsInInputOrder, bool throwInjectedFault = false) : IMessageComposer
{
    private static readonly TimeSpan BarrierLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HoldPerLaterRecord = TimeSpan.FromMilliseconds(150);

    private readonly TemplateMessageComposer template = new();
    private readonly TaskCompletionSource everyRecordComposing = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int composeCalls;

    public async Task<ComposeOutcome> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default)
    {
        // A record composes a second time only after its first compose returned, which needs
        // every record to have started, so the count reaches the batch size on the last
        // record's first call and never on a repeat.
        if (Interlocked.Increment(ref composeCalls) == taskIdsInInputOrder.Length)
        {
            everyRecordComposing.SetResult();
        }

        await everyRecordComposing.Task.WaitAsync(BarrierLimit, cancellationToken);

        int laterRecords = taskIdsInInputOrder.Length - 1 - Array.IndexOf(taskIdsInInputOrder, prospectCase.TaskId);
        await Task.Delay(HoldPerLaterRecord * laterRecords, cancellationToken);

        return throwInjectedFault
            ? throw new InvalidOperationException($"Injected fault for '{prospectCase.TaskId}'.")
            : await template.ComposeAsync(prospectCase, channel, priorViolations, cancellationToken);
    }
}
