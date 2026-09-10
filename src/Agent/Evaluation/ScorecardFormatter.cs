using System.Globalization;
using System.Text;
using Agent.Composition;

namespace Agent.Evaluation;

// Plain aligned text, not a table library: this is the artifact that proves the agent
// meets its thresholds (DESIGN.md section 6), read by a person. The per-check line is
// where the README numbers come from, so nobody counts cells by hand.
public static class ScorecardFormatter
{
    private static readonly (EvaluationCheck Check, string Label)[] Columns =
    [
        (EvaluationCheck.Channel, "Channel"),
        (EvaluationCheck.SendAtDay, "Day"),
        (EvaluationCheck.SendAtHour, "Hour"),
        (EvaluationCheck.NextActionType, "Action"),
        (EvaluationCheck.OptOut, "OptOut"),
        (EvaluationCheck.CtaType, "CTA"),
        (EvaluationCheck.CtaPayload, "Payload"),
        (EvaluationCheck.BodyLanguage, "Lang"),
        (EvaluationCheck.Safety, "Safety"),
        (EvaluationCheck.Personalization, "Personalization"),
        (EvaluationCheck.ActionSemantic, "ActionSem"),
        (EvaluationCheck.BodySemantic, "BodySem"),
    ];

    private static readonly string[] Headers =
        ["Task ID", .. Columns.Select(column => column.Label), "Latency (ms)", "Result"];

    public static string Format(Scorecard scorecard)
    {
        string[][] rows = scorecard.RecordScores.Select(FormatRow).ToArray();
        int[] widths = ComputeColumnWidths(rows);

        var builder = new StringBuilder();
        builder.AppendLine(FormatLine(Headers, widths));

        foreach (string[] row in rows)
        {
            builder.AppendLine(FormatLine(row, widths));
        }

        builder.AppendLine();
        builder.AppendLine("Checks: " + string.Join(", ", Columns.Select(column =>
            $"{column.Label} {scorecard.PassedCountOf(column.Check)}/{scorecard.MeasuredCountOf(column.Check)}")));
        builder.AppendLine($"Latency p95: {Milliseconds(scorecard.LatencyP95Ms)}, budget {Milliseconds(scorecard.LatencyBudgetMs)}: {Symbol(scorecard.LatencyP95)}");

        // D61 and D62, per batch: beside the p95 it already prints, because the p95 is computed
        // from the same per-record numbers and these two are the numbers they are not. Cost is
        // tokens, never money: the dated dollar figure lives in DESIGN.md section 9 and README.
        builder.AppendLine($"Batch latency: {Milliseconds(scorecard.BatchLatencyMs)}");
        builder.AppendLine($"Batch model cost: {ModelCostNotes.Describe(scorecard.BatchModelCost)}");
        builder.AppendLine($"Overall: {scorecard.PassedCount}/{scorecard.TotalCount} passed");

        return builder.ToString();
    }

    private static string[] FormatRow(RecordScore score) =>
        score.ScoringError is { } error
            ? [score.TaskId, .. Columns.Select(_ => "-"), "-", $"ERROR: {error}"]
            :
            [
                score.TaskId,
                .. Columns.Select(column => column.Check == EvaluationCheck.Personalization
                    ? PersonalizationCell(score)
                    : Symbol(score.ResultOf(column.Check))),
                score.LatencyMs is { } latency ? latency.ToString("0", CultureInfo.InvariantCulture) : "n/a",
                score.Passed ? "PASS" : "FAIL",
            ];

    private static string PersonalizationCell(RecordScore score) =>
        score.PersonalizationScore is { } value
            ? $"{value.ToString("0.00", CultureInfo.InvariantCulture)} {Symbol(score.Personalization)}"
            : "n/a";

    private static string Milliseconds(double? value) =>
        value is { } milliseconds ? $"{milliseconds.ToString("0", CultureInfo.InvariantCulture)} ms" : "n/a";

    private static int[] ComputeColumnWidths(string[][] rows)
    {
        var widths = new int[Headers.Length];

        for (int column = 0; column < Headers.Length; column++)
        {
            widths[column] = Headers[column].Length;
        }

        foreach (string[] row in rows)
        {
            for (int column = 0; column < row.Length; column++)
            {
                widths[column] = Math.Max(widths[column], row[column].Length);
            }
        }

        return widths;
    }

    private static string FormatLine(string[] cells, int[] widths) =>
        string.Join(" | ", cells.Select((cell, column) => cell.PadRight(widths[column])));

    private static string Symbol(CheckResult result) => result switch
    {
        CheckResult.Passed => "OK",
        CheckResult.Failed => "FAIL",
        _ => "n/a",
    };
}
