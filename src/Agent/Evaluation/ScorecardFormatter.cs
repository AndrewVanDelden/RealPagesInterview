using System.Text;

namespace Agent.Evaluation;

// Plain aligned text, not a table library: this is the artifact that proves the agent
// meets its thresholds (DESIGN.md section 6), read by a human during the live review -
// no dependency needed for a handful of columns.
public static class ScorecardFormatter
{
    private static readonly string[] Headers =
        ["Task ID", "Channel", "Action", "OptOut", "CTA", "Safety", "Personalization", "Latency (ms)", "Result"];

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
        builder.AppendLine($"Overall: {scorecard.PassedCount}/{scorecard.TotalCount} passed");

        return builder.ToString();
    }

    private static string[] FormatRow(RecordScore score) =>
        score.ScoringError is { } error
            ? [score.TaskId, "-", "-", "-", "-", "-", "-", "-", $"ERROR: {error}"]
            :
            [
                score.TaskId,
                Symbol(score.ChannelMatches),
                Symbol(score.NextActionTypeMatches),
                Symbol(score.OptOutPresent),
                Symbol(score.PrimaryCtaPresent),
                Symbol(score.SafetyViolationsWithinBudget),
                score.PersonalizationScore.ToString("0.00"),
                score.LatencyMs.ToString("0"),
                score.Passed ? "PASS" : "FAIL",
            ];

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

    private static string Symbol(bool value) => value ? "OK" : "FAIL";
}
