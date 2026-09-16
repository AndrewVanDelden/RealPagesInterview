using Agent.Composition;
using Agent.Domain;

namespace Agent.Cli.Tests.TestSupport;

// Cancels the run from inside its first compose call, so a test can show that the batch stops
// once the token is cancelled. The template path it composes through never observes the token
// itself, so a batch that did not check it would fold every record and close the output file.
internal sealed class CancelOnFirstComposeComposer(CancellationTokenSource runCancellation) : IMessageComposer
{
    private readonly TemplateMessageComposer template = new();
    private int calls;

    public Task<ComposeOutcome> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<DateTimeOffset>? tourSlots = null,
        DateOnly? referenceDate = null)
    {
        if (Interlocked.Increment(ref calls) == 1)
        {
            runCancellation.Cancel();
        }

        return template.ComposeAsync(prospectCase, channel, priorViolations, cancellationToken);
    }
}
