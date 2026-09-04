using Agent.Domain;

namespace Agent.Orchestration;

public interface IMessageAgent
{
    Task<AgentRunResult> RunAsync(ProspectCase prospectCase, CancellationToken cancellationToken = default);
}
