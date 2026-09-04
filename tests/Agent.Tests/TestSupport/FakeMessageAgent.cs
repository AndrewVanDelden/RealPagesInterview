using Agent.Domain;
using Agent.Orchestration;

namespace Agent.Tests.TestSupport;

internal sealed class FakeMessageAgent(AgentRunResult result, TimeSpan delay = default) : IMessageAgent
{
    public async Task<AgentRunResult> RunAsync(ProspectCase prospectCase, CancellationToken cancellationToken = default)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }

        return result;
    }
}
