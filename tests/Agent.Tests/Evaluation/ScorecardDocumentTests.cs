using System.Text.Json;
using Agent.Common;
using Agent.Composition;
using Agent.Evaluation;
using Xunit;

namespace Agent.Tests.Evaluation;

public class ScorecardDocumentTests
{
    // The machine-readable scorecard carries the same numbers the text report prints: the overall
    // count, a passed-over-measured tally per check under the report's own label, the p95 against
    // its budget, both model costs, and every row with each check's result by name.
    [Fact]
    public void From_Scorecard_CarriesTheReportsNumbersAndEveryRowsResults()
    {
        var scored = new RecordScore(
            "t1", CheckResult.Passed, CheckResult.Failed, CheckResult.Failed, CheckResult.Passed, CheckResult.Passed,
            CheckResult.Passed, CheckResult.NotMeasured, CheckResult.Passed, CheckResult.Passed, 0.5, CheckResult.Failed, 2500,
            ActionSemantic: CheckResult.Passed, BodySemantic: CheckResult.Failed, JudgeReason: "The body offers other days.");
        RecordScore unscoreable = RecordScore.DidNotParse("Line 2 failed to parse.");
        var scorecard = new Scorecard(
            [scored, unscoreable],
            latencyBudgetMs: 2000,
            batchLatencyMs: 4000,
            batchModelCost: new ModelCostNotes(Calls: 1, CompletedCalls: 1, InputTokens: 500, OutputTokens: 70),
            judgeModelCost: new ModelCostNotes(Calls: 1, CompletedCalls: 1, InputTokens: 480, OutputTokens: 100));

        ScorecardDocument document = ScorecardDocument.From(scorecard);

        Assert.Equal(0, document.Passed);
        Assert.Equal(2, document.Total);
        CheckTally day = Assert.Single(document.Checks, tally => tally.Check == "send_at_day");
        Assert.Equal(("Day", 0, 1), (day.Label, day.Passed, day.Measured));
        Assert.Equal(12, document.Checks.Count);
        Assert.Equal("failed", document.LatencyP95);
        Assert.Equal(2000, document.LatencyBudgetMs);
        Assert.Equal(4000, document.BatchLatencyMs);
        Assert.Equal(500, document.BatchModelCost!.InputTokens);
        Assert.Equal(100, document.JudgeModelCost!.OutputTokens);

        RecordScoreDocument first = document.Records[0];
        Assert.Equal("t1", first.TaskId);
        Assert.False(first.Passed);
        Assert.Equal("passed", first.Results["channel"]);
        Assert.Equal("failed", first.Results["send_at_day"]);
        Assert.Equal("not_measured", first.Results["cta_payload"]);
        Assert.Equal("failed", first.Results["body_semantic"]);
        Assert.Equal(0.5, first.PersonalizationScore);
        Assert.Equal(2500, first.LatencyMs);
        Assert.Equal("The body offers other days.", first.JudgeReason);
        Assert.Null(first.ScoringError);

        RecordScoreDocument second = document.Records[1];
        Assert.Contains("Line 2 failed to parse.", second.ScoringError);
        Assert.Null(second.LatencyMs);
    }

    // On the wire it is the project's own snake_case JSON, so a reader in another language needs
    // no naming rules of its own.
    [Fact]
    public void Serialize_WithAgentJsonOptions_WritesSnakeCaseMembers()
    {
        var scorecard = new Scorecard([RecordScore.DidNotParse("Line 1 failed to parse.")], latencyBudgetMs: null);

        string json = JsonSerializer.Serialize(ScorecardDocument.From(scorecard), AgentJsonOptions.Default);

        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Equal("not_measured", parsed.RootElement.GetProperty("latency_p95").GetString());
        Assert.Equal(JsonValueKind.Null, parsed.RootElement.GetProperty("batch_model_cost").ValueKind);
        JsonElement results = parsed.RootElement.GetProperty("records")[0].GetProperty("results");
        Assert.Equal(12, results.EnumerateObject().Count());
        Assert.All(results.EnumerateObject(), result => Assert.Equal("not_measured", result.Value.GetString()));
    }
}
