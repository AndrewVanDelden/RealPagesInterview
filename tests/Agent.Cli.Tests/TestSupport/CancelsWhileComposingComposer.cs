using Agent.Composition;
using Agent.Domain;

namespace Agent.Cli.Tests.TestSupport;

// Cancels the run from inside compose, then awaits the same token: unlike
// CancelOnFirstComposeComposer (which composes through the template and never observes the
// token, so only the *next* dispatch is stopped), this composer's own in-flight call is the one
// that gets cancelled, so a test can show what CliRunner does with a genuine mid-record
// cancellation rather than one it merely declined to start.
internal sealed class CancelsWhileComposingComposer(CancellationTokenSource runCancellation) : IMessageComposer
{
    public async Task<ComposeOutcome> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default)
    {
        runCancellation.Cancel();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("Should have thrown for cancellation before reaching here.");
    }
}
