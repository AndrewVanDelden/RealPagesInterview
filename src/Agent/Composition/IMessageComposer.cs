using Agent.Domain;

namespace Agent.Composition;

public interface IMessageComposer
{
    Task<ComposeOutcome> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default);
}
