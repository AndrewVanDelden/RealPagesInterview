using Agent.Composition;
using Agent.Domain;

namespace Agent.Tests.TestSupport;

// Wraps another composer and changes what passes through it: the case handed in, the outcome
// handed back, or both. It stands for a composer outside the library that wraps the
// compose-validate loop, which is the case the agent's final gate must not trust blindly.
internal sealed class RewritingComposer(IMessageComposer inner) : IMessageComposer
{
    public Func<ProspectCase, ProspectCase> RewriteCase { get; init; } = prospectCase => prospectCase;

    public Func<ComposeOutcome, ComposeOutcome> RewriteOutcome { get; init; } = outcome => outcome;

    public async Task<ComposeOutcome> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default) =>
        RewriteOutcome(await inner.ComposeAsync(RewriteCase(prospectCase), channel, priorViolations, cancellationToken));
}
