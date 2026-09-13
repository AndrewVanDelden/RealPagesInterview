using Agent.Composition;
using Agent.Domain;

namespace Agent.Cli.Tests.TestSupport;

// Shows whether the batch folds a finished record before it starts a later one. The record
// named faultTaskId throws at once; when the record named observerTaskId composes, this
// notes whether earlierRecordFolded, a task the test completes when the fold writes the
// failed record's stderr line, had already completed. Every other record, and the observer
// itself, composes through the real template composer.
internal sealed class FoldObservingComposer(string faultTaskId, string observerTaskId, Task earlierRecordFolded) : IMessageComposer
{
    private readonly TemplateMessageComposer template = new();

    public bool? EarlierRecordFoldedWhenObserverComposed { get; private set; }

    public Task<ComposeOutcome> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default)
    {
        if (prospectCase.TaskId == faultTaskId)
        {
            throw new InvalidOperationException($"Injected fault for '{faultTaskId}'.");
        }

        if (prospectCase.TaskId == observerTaskId)
        {
            EarlierRecordFoldedWhenObserverComposed = earlierRecordFolded.IsCompleted;
        }

        return template.ComposeAsync(prospectCase, channel, priorViolations, cancellationToken);
    }
}
