using Agent.Composition;
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

        var scorecard = new Scorecard(scores, latencyBudgetMs: 2000);

        Assert.Equal(19, scorecard.LatencyP95Ms);
        Assert.Equal(CheckResult.Passed, scorecard.LatencyP95);
    }

    [Fact]
    public void LatencyP95Ms_IgnoresRecordsWithNoLatency()
    {
        var scorecard = new Scorecard([Score("t1", latencyMs: 40), Score("t2", latencyMs: null)], latencyBudgetMs: 30);

        Assert.Equal(40, scorecard.LatencyP95Ms);
        Assert.Equal(CheckResult.Failed, scorecard.LatencyP95);
    }

    [Fact]
    public void LatencyP95_NoLatencyMeasured_NotMeasured()
    {
        var scorecard = new Scorecard([Score("t1", latencyMs: null)], latencyBudgetMs: 2000);

        Assert.Null(scorecard.LatencyP95Ms);
        Assert.Equal(CheckResult.NotMeasured, scorecard.LatencyP95);
    }

    [Fact]
    public void LatencyP95_NoBudgetStated_NotMeasured()
    {
        var scorecard = new Scorecard([Score("t1", latencyMs: 5)], latencyBudgetMs: null);

        Assert.Equal(5, scorecard.LatencyP95Ms);
        Assert.Equal(CheckResult.NotMeasured, scorecard.LatencyP95);
    }

    [Fact]
    public void AllPassed_EveryRecordPassedButP95Failed_False()
    {
        var scorecard = new Scorecard([Score("t1", latencyMs: 100)], latencyBudgetMs: 50);

        Assert.Equal(1, scorecard.PassedCount);
        Assert.False(scorecard.AllPassed);
    }

    [Fact]
    public void AllPassed_EveryRecordPassedAndP95NotMeasured_True()
    {
        var scorecard = new Scorecard([Score("t1", latencyMs: null)], latencyBudgetMs: 50);

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
            latencyBudgetMs: null);

        Assert.Equal(1, scorecard.PassedCountOf(EvaluationCheck.Channel));
        Assert.Equal(2, scorecard.MeasuredCountOf(EvaluationCheck.Channel));
        Assert.Equal(2, scorecard.PassedCountOf(EvaluationCheck.SendAtDay));
        Assert.Equal(2, scorecard.MeasuredCountOf(EvaluationCheck.SendAtDay));
    }

    // The tallies are computed once from the rows, so the rows must not change after that:
    // a caller that keeps and edits the list it passed in must not move the rows the
    // scorecard reports away from the totals it reports.
    [Fact]
    public void Constructor_CallerEditsItsListAfterward_RowsAndTalliesStillAgree()
    {
        var rows = new List<RecordScore> { Score("t1") };
        var scorecard = new Scorecard(rows, latencyBudgetMs: null);

        rows.Add(Score("t2", channel: CheckResult.Failed));

        Assert.Single(scorecard.RecordScores);
        Assert.Equal(1, scorecard.TotalCount);
        Assert.Equal(1, scorecard.MeasuredCountOf(EvaluationCheck.Channel));
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

    // An input the batch could not process is a row, never a silence. It counts in the
    // overall and never passes, and it measures no check, so no per-check tally and no latency
    // number moves, and the batch numbers the run measured ride along unchanged.
    [Fact]
    public void AppendUnprocessed_RowCountsInOverallWithoutMovingAnyTallyOrBatchNumber()
    {
        var batchModelCost = new ModelCostNotes(2, 2, 100, 40);
        var scorecard = new Scorecard(
            [Score("t1", latencyMs: 40), Score("t2", latencyMs: 10)],
            latencyBudgetMs: 2000,
            batchLatencyMs: 55,
            batchModelCost: batchModelCost);

        Scorecard appended = scorecard.AppendUnprocessed([RecordScore.Unscoreable("t3", "Record failed: InvalidOperationException")]);

        Assert.Equal(3, appended.TotalCount);
        Assert.Equal(2, appended.PassedCount);
        Assert.False(appended.AllPassed);
        Assert.Equal("t3", appended.RecordScores[2].TaskId);
        Assert.All(EvaluationChecks.All, check => Assert.Equal(scorecard.MeasuredCountOf(check), appended.MeasuredCountOf(check)));
        Assert.Equal(40, appended.LatencyP95Ms);
        Assert.Equal(2000, appended.LatencyBudgetMs);
        Assert.Equal(55, appended.BatchLatencyMs);
        Assert.Same(batchModelCost, appended.BatchModelCost);
    }

    // A line that did not parse has no task id to show, since the id is what failed to
    // parse; the reader's own failure text names the line instead.
    [Fact]
    public void DidNotParse_NamesTheLineThroughTheReadersFailureAndNeverPasses()
    {
        const string readFailure = "Line 11 failed to parse: JsonException: line 0, byte position 96";

        RecordScore score = RecordScore.DidNotParse(readFailure);

        Assert.Equal("(did not parse)", score.TaskId);
        Assert.Equal(readFailure, score.ScoringError);
        Assert.False(score.Passed);
    }
}
