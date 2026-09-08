using Agent.Domain;
using Agent.Evaluation;

namespace Agent.Tests.TestSupport;

// Playbook step 33: the labeled answers passed as the actual outputs. A label carries no
// safety count or latency; the golden run states zero violations (the oracle is clean by
// definition) and no latency (not measured).
internal static class GoldenRuns
{
    public static IReadOnlyList<ScoredRun> FromLabels(IReadOnlyList<ProspectCase> cases) =>
        cases.Select(FromLabel).ToList();

    public static ScoredRun FromLabel(ProspectCase prospectCase)
    {
        ExpectedOutcome expected = prospectCase.Expected ?? throw new InvalidOperationException($"'{prospectCase.TaskId}' carries no label.");

        return new ScoredRun(prospectCase, new AgentOutput(expected.NextMessage, expected.NextAction), SafetyViolationCount: 0, LatencyMs: null);
    }
}
