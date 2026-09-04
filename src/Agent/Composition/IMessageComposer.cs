using Agent.Common;
using Agent.Domain;

namespace Agent.Composition;

public interface IMessageComposer
{
    Task<Result<NextMessage>> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default);
}
