using Agent.Composition;
using Agent.Evaluation;
using Xunit;

namespace Agent.Tests.Evaluation;

public class ScorecardFormatterTests
{
    private static RecordScore PassingScore(string taskId = "t1") =>
        new(taskId, CheckResult.Passed, CheckResult.Passed, CheckResult.Passed, CheckResult.Passed, CheckResult.Passed,
            CheckResult.Passed, CheckResult.Passed, CheckResult.Passed, CheckResult.Passed, 1.0, CheckResult.Passed, 12);

    private static RecordScore FailingScore(string taskId = "t2") =>
        new(taskId, CheckResult.Failed, CheckResult.Passed, CheckResult.NotMeasured, CheckResult.Passed, CheckResult.Failed,
            CheckResult.Passed, CheckResult.NotMeasured, CheckResult.Passed, CheckResult.Passed, 0.5, CheckResult.Failed, 3000);

    private static RecordScore UnmeasuredScore(string taskId = "t5") =>
        new(taskId, CheckResult.Passed, CheckResult.NotMeasured, CheckResult.NotMeasured, CheckResult.Passed, CheckResult.NotMeasured,
            CheckResult.NotMeasured, CheckResult.NotMeasured, CheckResult.NotMeasured, CheckResult.NotMeasured, null, CheckResult.NotMeasured, null);

    [Fact]
    public void Format_PassingRecord_ShowsTaskIdAndPassResult()
    {
        var scorecard = new Scorecard([PassingScore("prospect_welcome_day0")], 2000);

        string report = ScorecardFormatter.Format(scorecard);

        string row = Assert.Single(report.Split('\n'), line => line.StartsWith("prospect_welcome_day0", StringComparison.Ordinal));
        Assert.Contains("PASS", row);
        Assert.DoesNotContain("FAIL", row);
    }

    [Fact]
    public void Format_FailingRecord_ShowsFailResultAndFailCells()
    {
        var scorecard = new Scorecard([FailingScore()], 2000);

        string report = ScorecardFormatter.Format(scorecard);

        Assert.Contains("FAIL", report);
        Assert.Contains("0.50 FAIL", report);
        Assert.Contains("n/a", report);
    }

    [Fact]
    public void Format_UnmeasuredRecord_ShowsNotApplicableForScoreAndLatency()
    {
        var scorecard = new Scorecard([UnmeasuredScore()], null);

        string[] lines = ScorecardFormatter.Format(scorecard).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("n/a", lines[1]);
        Assert.DoesNotContain("FAIL", lines[1]);
        Assert.Contains("PASS", lines[1]);
    }

    [Fact]
    public void Format_MultipleRecords_ShowsOverallPassedCount()
    {
        var scorecard = new Scorecard([PassingScore("t1"), FailingScore("t2")], 2000);

        string report = ScorecardFormatter.Format(scorecard);

        Assert.Contains("Overall: 1/2 passed", report);
    }

    [Fact]
    public void Format_PerCheckLine_ShowsPassedOverMeasured()
    {
        var scorecard = new Scorecard([PassingScore("t1"), FailingScore("t2")], 2000);

        string report = ScorecardFormatter.Format(scorecard);

        Assert.Contains("Channel 1/2", report);
        Assert.Contains("Hour 1/1", report);
        Assert.Contains("Payload 1/1", report);
        Assert.Contains("Personalization 1/2", report);
    }

    [Fact]
    public void Format_LatencyLine_ShowsP95AgainstTheBudget()
    {
        var scorecard = new Scorecard([PassingScore("t1"), FailingScore("t2")], 2000);

        string report = ScorecardFormatter.Format(scorecard);

        Assert.Contains("Latency p95: 3000 ms, budget 2000 ms: FAIL", report);
    }

    [Fact]
    public void Format_LatencyNotMeasured_SaysSo()
    {
        var scorecard = new Scorecard([UnmeasuredScore()], null);

        string report = ScorecardFormatter.Format(scorecard);

        Assert.Contains("Latency p95: n/a, budget n/a: n/a", report);
    }

    [Fact]
    public void Format_NoRecords_ShowsZeroOfZero()
    {
        var scorecard = new Scorecard([], null);

        string report = ScorecardFormatter.Format(scorecard);

        Assert.Contains("Overall: 0/0 passed", report);
    }

    [Fact]
    public void Format_UnscoreableRecord_ShowsErrorMessageInsteadOfColumns()
    {
        var scorecard = new Scorecard([RecordScore.Unscoreable("t3", "no expected outcome")], null);

        string report = ScorecardFormatter.Format(scorecard);

        Assert.Contains("t3", report);
        Assert.Contains("ERROR: no expected outcome", report);
    }

    [Fact]
    public void Format_VaryingTaskIdLengths_PadsColumnsToAlign()
    {
        var scorecard = new Scorecard([PassingScore("t1"), PassingScore("prospect_welcome_day0")], 2000);

        string[] lines = ScorecardFormatter.Format(scorecard).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        int shortRowFirstPipeIndex = lines[1].IndexOf('|');
        int longRowFirstPipeIndex = lines[2].IndexOf('|');
        Assert.Equal(shortRowFirstPipeIndex, longRowFirstPipeIndex);
    }

    // Latency per batch: one wall-clock elapsed around the record loop, printed beside the p95 it
    // is not. The p95 is computed from the per-record numbers; this one is measured once.
    [Fact]
    public void Format_BatchWasTimed_PrintsTheBatchElapsed()
    {
        var scorecard = new Scorecard([PassingScore("t1")], 2000, batchLatencyMs: 250);

        string report = ScorecardFormatter.Format(scorecard);

        Assert.Contains("Batch latency: 250 ms", report);
    }

    // --replay runs no record loop (it scores an existing output file against the input), so
    // nothing was timed and nothing was spent. Both batch lines say so rather than printing a
    // zero nobody measured.
    [Fact]
    public void Format_BatchWasNotTimedOrCosted_SaysSoOnBothBatchLines()
    {
        var scorecard = new Scorecard([UnmeasuredScore()], null);

        string report = ScorecardFormatter.Format(scorecard);

        Assert.Contains("Batch latency: n/a", report);
        Assert.Contains("Batch model cost: none", report);
    }

    // Cost per batch: the totals the batch spent, in tokens, rendered by the one description
    // the Batch complete log line uses too.
    [Fact]
    public void Format_BatchSpentTokens_PrintsEveryCountItMeasured()
    {
        var scorecard = new Scorecard(
            [PassingScore("t1")],
            2000,
            batchModelCost: new ModelCostNotes(Calls: 3, CompletedCalls: 2, InputTokens: 22, OutputTokens: 14));

        string report = ScorecardFormatter.Format(scorecard);

        Assert.Contains("Batch model cost: 3 call(s), 2 completed, 22 input + 14 output token(s)", report);
    }
}
