using Agent.Common;
using Agent.Composition;
using Agent.Domain;

namespace Agent.Tests.TestSupport;

// Scripted results, one per call, the last one repeating. Tests state the messages they
// care about, so the composition notes D24 added are filled in here: this fake names itself,
// which is what lets a test tell an inner-composer answer apart from the fallback's.
internal sealed class SequenceMessageComposer(params Result<NextMessage>[] results) : IMessageComposer
{
    public const string Name = "sequence";

    public int CallCount { get; private set; }

    public IReadOnlyList<string>? LastPriorViolations { get; private set; }

    public Task<Result<ComposedMessage>> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default)
    {
        LastPriorViolations = priorViolations;
        Result<NextMessage> result = results[Math.Min(CallCount, results.Length - 1)];
        CallCount++;

        return Task.FromResult(result.IsSuccess
            ? Result<ComposedMessage>.Success(new ComposedMessage(result.Value, new CompositionNotes(Name, Attempts: 1)))
            : Result<ComposedMessage>.Failure(result.Error));
    }
}
