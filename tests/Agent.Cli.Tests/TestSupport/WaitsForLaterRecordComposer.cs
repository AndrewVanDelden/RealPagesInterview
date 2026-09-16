using Agent.Composition;
using Agent.Domain;

namespace Agent.Cli.Tests.TestSupport;

// Shows whether the batch starts a later record while an earlier one is still composing. The
// record named waitingTaskId does not finish composing until the record named laterTaskId has
// started, or until the wait gives up; LaterRecordStartedWhileWaiting says which happened. A batch
// that held back later records until earlier ones finished would make the wait give up. Every
// record composes through the real template composer.
internal sealed class WaitsForLaterRecordComposer(string waitingTaskId, string laterTaskId) : IMessageComposer
{
    private static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(10);

    private readonly TemplateMessageComposer template = new();
    private readonly TaskCompletionSource laterRecordStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool LaterRecordStartedWhileWaiting { get; private set; }

    public async Task<ComposeOutcome> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<DateTimeOffset>? tourSlots = null,
        DateOnly? referenceDate = null)
    {
        if (prospectCase.TaskId == laterTaskId)
        {
            laterRecordStarted.TrySetResult();
        }

        if (prospectCase.TaskId == waitingTaskId)
        {
            Task finished = await Task.WhenAny(laterRecordStarted.Task, Task.Delay(MaximumWait, cancellationToken));
            LaterRecordStartedWhileWaiting = finished == laterRecordStarted.Task;
        }

        return await template.ComposeAsync(prospectCase, channel, priorViolations, cancellationToken);
    }
}
