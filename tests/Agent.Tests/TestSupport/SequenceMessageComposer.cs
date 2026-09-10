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

    // One retry count per call, index-clamped like results itself; empty means every call
    // reports no network retries at all (the default a fake with nothing to say about
    // retries should have).
    public IReadOnlyList<int?> NetworkRetries { get; init; } = [];

    // One measured model cost per call, index-clamped the same way (D62). Empty means no call
    // this fake made went near a model, which is the null a composer with no network call of
    // its own reports. It rides on the composed notes and on the failure alike, because an
    // abandoned call is a cost with no message behind it.
    public IReadOnlyList<ModelCostNotes?> ModelCosts { get; init; } = [];

    public Task<ComposeOutcome> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default)
    {
        LastPriorViolations = priorViolations;
        Result<NextMessage> result = results[Math.Min(CallCount, results.Length - 1)];
        int? networkRetries = NetworkRetries.Count == 0 ? null : NetworkRetries[Math.Min(CallCount, NetworkRetries.Count - 1)];
        ModelCostNotes? modelCost = ModelCosts.Count == 0 ? null : ModelCosts[Math.Min(CallCount, ModelCosts.Count - 1)];
        CallCount++;

        // The scripted results stay Result<NextMessage>: a test scripts the messages it cares
        // about, and a composer that could not build one is the seam's Failed case. No
        // composer in this program refuses its own draft, so this fake does not either.
        // D66: the two counts ride on the outcome, so this fake states them once for either
        // case rather than putting them on notes only a composed message has.
        return Task.FromResult<ComposeOutcome>(result.IsSuccess
            ? new ComposeOutcome.Composed(new ComposedMessage(result.Value, CompositionNotes.ForComposer(Name, localeApplied: true)))
            {
                NetworkRetries = networkRetries,
                ModelCost = modelCost,
            }
            : new ComposeOutcome.Failed(result.Error) { NetworkRetries = networkRetries, ModelCost = modelCost });
    }
}
