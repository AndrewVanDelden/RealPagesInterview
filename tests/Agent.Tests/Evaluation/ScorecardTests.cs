using Agent.Evaluation;
using Xunit;

namespace Agent.Tests.Evaluation;

public class ScorecardTests
{
    private static RecordScore Score(string taskId, CheckResult every = CheckResult.Passed, double? latencyMs = 10, CheckResult? channel = null) =>
        new(taskId, channel ?? every, every, every, every, every, every, every, every, every, 1.0, every, latencyMs);

    [Fact]
    public void LatencyP95Ms_NearestRankOverMeasuredLatencies()
    {
        RecordScore[] scores = Enumerable.Range(1, 20).Select(i => Score($"t{i}", latencyMs: i)).ToArray();

        var scorecard = new Scorecard(scores, LatencyBudgetMs: 2000);

        Assert.Equal(19, scorecard.LatencyP95Ms);
        Assert.Equal(CheckResult.Passed, scorecard.LatencyP95);
    }

    [Fact]
    public void LatencyP95Ms_IgnoresRecordsWithNoLatency()
    {
        var scorecard = new Scorecard([Score("t1", latencyMs: 40), Score("t2", latencyMs: null)], LatencyBudgetMs: 30);

        Assert.Equal(40, scorecard.LatencyP95Ms);
        Assert.Equal(CheckResult.Failed, scorecard.LatencyP95);
    }

    [Fact]
    public void LatencyP95_NoLatencyMeasured_NotMeasured()
    {
        var scorecard = new Scorecard([Score("t1", latencyMs: null)], LatencyBudgetMs: 2000);

        Assert.Null(scorecard.LatencyP95Ms);
        Assert.Equal(CheckResult.NotMeasured, scorecard.LatencyP95);
    }

    [Fact]
    public void LatencyP95_NoBudgetStated_NotMeasured()
    {
        var scorecard = new Scorecard([Score("t1", latencyMs: 5)], LatencyBudgetMs: null);

        Assert.Equal(5, scorecard.LatencyP95Ms);
        Assert.Equal(CheckResult.NotMeasured, scorecard.LatencyP95);
    }

    [Fact]
    public void AllPassed_EveryRecordPassedButP95Failed_False()
    {
        var scorecard = new Scorecard([Score("t1", latencyMs: 100)], LatencyBudgetMs: 50);

        Assert.Equal(1, scorecard.PassedCount);
        Assert.False(scorecard.AllPassed);
    }

    [Fact]
    public void AllPassed_EveryRecordPassedAndP95NotMeasured_True()
    {
        var scorecard = new Scorecard([Score("t1", latencyMs: null)], LatencyBudgetMs: 50);

        Assert.True(scorecard.AllPassed);
    }

    [Fact]
    public void PassedAndMeasuredCount_PerCheck_CountOnlyMeasuredRecords()
    {
        var scorecard = new Scorecard(
            [
                Score("t1"),
                Score("t2", channel: CheckResult.Failed),
                Score("t3", every: CheckResult.NotMeasured),
                RecordScore.Unscoreable("t4", "no label"),
            ],
            LatencyBudgetMs: null);

        Assert.Equal(1, scorecard.PassedCountOf(EvaluationCheck.Channel));
        Assert.Equal(2, scorecard.MeasuredCountOf(EvaluationCheck.Channel));
        Assert.Equal(2, scorecard.PassedCountOf(EvaluationCheck.SendAtDay));
        Assert.Equal(2, scorecard.MeasuredCountOf(EvaluationCheck.SendAtDay));
    }

    [Fact]
    public void ResultOf_EveryCheck_ReadsTheMatchingMember()
    {
        RecordScore score = new("t1",
            Channel: CheckResult.Passed,
            SendAtDay: CheckResult.Failed,
            SendAtHour: CheckResult.NotMeasured,
            NextActionType: CheckResult.Passed,
            OptOut: CheckResult.Failed,
            CtaType: CheckResult.NotMeasured,
            CtaPayload: CheckResult.Passed,
            BodyLanguage: CheckResult.Failed,
            Safety: CheckResult.NotMeasured,
            PersonalizationScore: 0.5,
            Personalization: CheckResult.Passed,
            LatencyMs: 1);

        Assert.Equal(CheckResult.Passed, score.ResultOf(EvaluationCheck.Channel));
        Assert.Equal(CheckResult.Failed, score.ResultOf(EvaluationCheck.SendAtDay));
        Assert.Equal(CheckResult.NotMeasured, score.ResultOf(EvaluationCheck.SendAtHour));
        Assert.Equal(CheckResult.Passed, score.ResultOf(EvaluationCheck.NextActionType));
        Assert.Equal(CheckResult.Failed, score.ResultOf(EvaluationCheck.OptOut));
        Assert.Equal(CheckResult.NotMeasured, score.ResultOf(EvaluationCheck.CtaType));
        Assert.Equal(CheckResult.Passed, score.ResultOf(EvaluationCheck.CtaPayload));
        Assert.Equal(CheckResult.Failed, score.ResultOf(EvaluationCheck.BodyLanguage));
        Assert.Equal(CheckResult.NotMeasured, score.ResultOf(EvaluationCheck.Safety));
        Assert.Equal(CheckResult.Passed, score.ResultOf(EvaluationCheck.Personalization));
    }

    [Fact]
    public void ResultOf_UndefinedCheck_Throws()
    {
        RecordScore score = Score("t1");

        Assert.Throws<ArgumentOutOfRangeException>(() => score.ResultOf((EvaluationCheck)999));
    }

    [Fact]
    public void Unscoreable_EveryCheckNotMeasuredAndNotPassed()
    {
        RecordScore score = RecordScore.Unscoreable("t1", "reason");

        Assert.Equal("reason", score.ScoringError);
        Assert.False(score.Passed);
        Assert.Null(score.PersonalizationScore);
        Assert.Null(score.LatencyMs);
        Assert.All(Enum.GetValues<EvaluationCheck>(), check => Assert.Equal(CheckResult.NotMeasured, score.ResultOf(check)));
    }
}
