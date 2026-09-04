using Agent.Evaluation;
using Xunit;

namespace Agent.Tests.Evaluation;

public class ScorecardFormatterTests
{
    private static RecordScore PassingScore(string taskId = "t1") =>
        new(taskId, ChannelMatches: true, NextActionTypeMatches: true, OptOutPresent: true, PrimaryCtaPresent: true,
            SafetyViolationsWithinBudget: true, PersonalizationScore: 1.0, PersonalizationScoreMet: true,
            LatencyMs: 12, LatencyWithinBudget: true);

    private static RecordScore FailingScore(string taskId = "t2") =>
        new(taskId, ChannelMatches: false, NextActionTypeMatches: true, OptOutPresent: false, PrimaryCtaPresent: true,
            SafetyViolationsWithinBudget: true, PersonalizationScore: 0.5, PersonalizationScoreMet: false,
            LatencyMs: 3000, LatencyWithinBudget: false);

    [Fact]
    public void Format_PassingRecord_ShowsTaskIdAndPassResult()
    {
        var scorecard = new Scorecard([PassingScore("prospect_welcome_day0")]);

        string report = ScorecardFormatter.Format(scorecard);

        Assert.Contains("prospect_welcome_day0", report);
        Assert.Contains("PASS", report);
    }

    [Fact]
    public void Format_FailingRecord_ShowsFailResult()
    {
        var scorecard = new Scorecard([FailingScore()]);

        string report = ScorecardFormatter.Format(scorecard);

        Assert.Contains("FAIL", report);
    }

    [Fact]
    public void Format_MultipleRecords_ShowsOverallPassedCount()
    {
        var scorecard = new Scorecard([PassingScore("t1"), FailingScore("t2")]);

        string report = ScorecardFormatter.Format(scorecard);

        Assert.Contains("1/2", report);
    }

    [Fact]
    public void Format_NoRecords_ShowsZeroOfZero()
    {
        var scorecard = new Scorecard([]);

        string report = ScorecardFormatter.Format(scorecard);

        Assert.Contains("0/0", report);
    }

    [Fact]
    public void Format_UnscoreableRecord_ShowsErrorMessageInsteadOfColumns()
    {
        var scorecard = new Scorecard([RecordScore.Unscoreable("t3", "no expected outcome")]);

        string report = ScorecardFormatter.Format(scorecard);

        Assert.Contains("t3", report);
        Assert.Contains("ERROR: no expected outcome", report);
    }

    [Fact]
    public void Format_VaryingTaskIdLengths_PadsColumnsToAlign()
    {
        var scorecard = new Scorecard([PassingScore("t1"), PassingScore("prospect_welcome_day0")]);

        string[] lines = ScorecardFormatter.Format(scorecard).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        int shortRowFirstPipeIndex = lines[1].IndexOf('|');
        int longRowFirstPipeIndex = lines[2].IndexOf('|');
        Assert.Equal(shortRowFirstPipeIndex, longRowFirstPipeIndex);
    }
}
