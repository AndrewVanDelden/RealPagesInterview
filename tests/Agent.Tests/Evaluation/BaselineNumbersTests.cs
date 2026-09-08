using Agent.Domain;
using Agent.Evaluation;
using Agent.Orchestration;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Evaluation;

// Playbook step 36: the scorecard is wired into CI so a regression in any per-check number
// fails the build. These are measurements, not targets (D6, D9): a number that rises is
// recorded here and in README.md together, never chased. The template composer runs on every
// set with the set's documented reference time; latency is not measured in the suite.
public class BaselineNumbersTests
{
    [Theory]
    [InlineData(
        "sample.jsonl",
        "2025-12-09T00:00:00-06:00",
        "Checks: Channel 2/2, Day 2/2, Hour 2/2, Action 2/2, OptOut 2/2, CTA 2/2, Payload 2/2, Lang 2/2, Safety 2/2, Personalization 2/2",
        "Overall: 2/2 passed")]
    [InlineData(
        "holdout_12.jsonl",
        "2025-12-09T00:00:00-06:00",
        "Checks: Channel 12/12, Day 7/11, Hour 5/11, Action 7/12, OptOut 11/11, CTA 7/11, Payload 11/11, Lang 10/11, Safety 12/12, Personalization 8/8",
        "Overall: 3/12 passed")]
    [InlineData(
        "synthetic_12.jsonl",
        "2026-03-07T12:00:00Z",
        "Checks: Channel 12/12, Day 10/10, Hour 10/10, Action 12/12, OptOut 10/10, CTA 10/10, Payload 10/10, Lang 9/10, Safety 12/12, Personalization 9/9",
        "Overall: 11/12 passed")]
    public async Task TemplateAgent_OnEachLabeledSet_ScoresTheRecordedBaseline(string fileName, string referenceTime, string expectedChecksLine, string expectedOverallLine)
    {
        IReadOnlyList<ProspectCase> cases = RealAgentFactory.ReadCases(fileName);
        IMessageAgent agent = RealAgentFactory.BuildRealAgent();
        DateTimeOffset now = DateTimeOffset.Parse(referenceTime);
        var runs = new List<ScoredRun>(cases.Count);

        foreach (ProspectCase prospectCase in cases)
        {
            AgentRunResult result = await agent.RunAsync(prospectCase, now);
            runs.Add(new ScoredRun(prospectCase, result.Output, result.Diagnostics.SafetyViolationCount, LatencyMs: null));
        }

        string report = ScorecardFormatter.Format(new Evaluator().Evaluate(runs));

        Assert.Contains(expectedChecksLine, report);
        Assert.Contains(expectedOverallLine, report);
    }
}
