using Agent.Common;
using Agent.Composition;

namespace Agent.Evaluation;

// The scorecard as data, for a program to read rather than a person: the same numbers the text
// report prints, with every check and result named by its snake_case wire name and each check's
// tally carrying the text report's own label, so a chart and the report say the same word.
public sealed record ScorecardDocument(
    int Passed,
    int Total,
    IReadOnlyList<CheckTally> Checks,
    double? LatencyP95Ms,
    int? LatencyBudgetMs,
    string LatencyP95,
    double? BatchLatencyMs,
    ModelCostNotes? BatchModelCost,
    ModelCostNotes? JudgeModelCost,
    IReadOnlyList<RecordScoreDocument> Records)
{
    // O(n × c) in the rows and the twelve checks.
    public static ScorecardDocument From(Scorecard scorecard)
    {
        // The wire name of each check is the same for every record, so it is computed once here
        // rather than once per record inside the record Select below.
        (EvaluationCheck Check, string WireName)[] checkWireNames =
            [.. ScorecardFormatter.Columns.Select(column => (column.Check, WireName(column.Check)))];

        return new(
            scorecard.PassedCount,
            scorecard.TotalCount,
            [.. ScorecardFormatter.Columns.Select(column => new CheckTally(
                WireName(column.Check),
                column.Label,
                scorecard.PassedCountOf(column.Check),
                scorecard.MeasuredCountOf(column.Check)))],
            scorecard.LatencyP95Ms,
            scorecard.LatencyBudgetMs,
            WireName(scorecard.LatencyP95),
            scorecard.BatchLatencyMs,
            scorecard.BatchModelCost,
            scorecard.JudgeModelCost,
            [.. scorecard.RecordScores.Select(score => new RecordScoreDocument(
                score.TaskId,
                score.Passed,
                checkWireNames.ToDictionary(entry => entry.WireName, entry => WireName(score.ResultOf(entry.Check))),
                score.PersonalizationScore,
                score.LatencyMs,
                score.ScoringError,
                score.JudgeReason))]);
    }

    private static string WireName<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        SnakeCaseLowerEnumConverter<TEnum>.ToWireName(value);
}

// One check's passed-over-measured tally across the batch; a check that was not measured on a row
// is in neither count.
public sealed record CheckTally(string Check, string Label, int Passed, int Measured);

// One row: its result per check by wire name, and what the text report shows beside them.
public sealed record RecordScoreDocument(
    string TaskId,
    bool Passed,
    IReadOnlyDictionary<string, string> Results,
    double? PersonalizationScore,
    double? LatencyMs,
    string? ScoringError,
    string? JudgeReason);
