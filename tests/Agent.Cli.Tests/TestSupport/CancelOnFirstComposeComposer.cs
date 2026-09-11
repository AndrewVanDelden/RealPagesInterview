using Agent.Composition;
using Agent.Domain;

namespace Agent.Cli.Tests.TestSupport;

// Cancels the run from inside its first record, so a test can show that the batch loop
// stops starting records once the token is cancelled. The template path it composes through
// never observes the token itself, so a loop that did not check it would run the whole batch.
internal sealed class CancelOnFirstComposeComposer(CancellationTokenSource runCancellation) : IMessageComposer
{
    private readonly TemplateMessageComposer template = new();
    private int calls;

    public int Calls => Volatile.Read(ref calls);

    public Task<ComposeOutcome> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref calls) == 1)
        {
            runCancellation.Cancel();
        }

        return template.ComposeAsync(prospectCase, channel, priorViolations, cancellationToken);
    }
}
