using Agent.Domain;

namespace Agent.Orchestration;

public interface IMessageAgent
{
    // referenceTime is the run's clock (D10): a value the caller passes, never read here.
    Task<AgentRunResult> RunAsync(ProspectCase prospectCase, DateTimeOffset referenceTime, CancellationToken cancellationToken = default);
}
