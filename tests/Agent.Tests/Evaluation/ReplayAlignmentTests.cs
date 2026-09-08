using Agent.Common;
using Agent.Domain;
using Agent.Evaluation;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Evaluation;

public class ReplayAlignmentTests
{
    private static AgentOutput Output(string actionType) => new(null, new NextAction(actionType));

    [Fact]
    public void Align_EqualCounts_PairsByPositionWithNothingMeasuredButTheOutput()
    {
        ProspectCase first = SampleProspectCases.Minimal() with { TaskId = "first" };
        ProspectCase second = SampleProspectCases.Minimal() with { TaskId = "second" };

        Result<IReadOnlyList<ScoredRun>> result = ReplayAlignment.Align([first, second], [Output("a"), Output("b")]);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value,
            run =>
            {
                Assert.Same(first, run.ProspectCase);
                Assert.Equal("a", run.Output.NextAction.Type);
                Assert.Null(run.SafetyViolationCount);
                Assert.Null(run.LatencyMs);
            },
            run =>
            {
                Assert.Same(second, run.ProspectCase);
                Assert.Equal("b", run.Output.NextAction.Type);
            });
    }

    [Fact]
    public void Align_NoRecords_SuccessWithNoRuns()
    {
        Result<IReadOnlyList<ScoredRun>> result = ReplayAlignment.Align([], []);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Align_CountsDiffer_FailureNamingBothCounts()
    {
        Result<IReadOnlyList<ScoredRun>> result = ReplayAlignment.Align([SampleProspectCases.Minimal()], [Output("a"), Output("b")]);

        Assert.False(result.IsSuccess);
        Assert.Contains("1 record", result.Error);
        Assert.Contains("2 output", result.Error);
    }
}
