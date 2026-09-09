using Agent.Common;
using Agent.Composition;
using Agent.Domain;

namespace Agent.Cli.Tests.TestSupport;

// Fault injection for the batch loop's per-record isolation: throws for one task id and
// composes a plain message for every other record. No input can make a record throw any
// more (every member is optional and every decision has a default), so a bug is the only
// remaining way a record fails, and this stands in for that bug.
internal sealed class ThrowingComposer(string taskIdToFail) : IMessageComposer
{
    public Task<Result<ComposedMessage>> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default)
    {
        if (prospectCase.TaskId == taskIdToFail)
        {
            throw new InvalidOperationException($"Injected fault for '{taskIdToFail}'.");
        }

        var message = new NextMessage(channel, null, null, "Hi. Reply STOP to opt out.", new Cta("reply", null, null));
        var composed = new ComposedMessage(message, CompositionNotes.ForComposer(ComposerNames.Template, localeApplied: true));

        return Task.FromResult(Result<ComposedMessage>.Success(composed));
    }
}
