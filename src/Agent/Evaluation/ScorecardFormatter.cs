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
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(" | ", Headers));

        foreach (RecordScore score in scorecard.RecordScores)
        {
            builder.AppendLine(string.Join(
                " | ",
                score.TaskId,
                Symbol(score.ChannelMatches),
                Symbol(score.NextActionTypeMatches),
                Symbol(score.OptOutPresent),
                Symbol(score.PrimaryCtaPresent),
                Symbol(score.SafetyViolationsWithinBudget),
                score.PersonalizationScore.ToString("0.00"),
                score.LatencyMs.ToString("0"),
                score.Passed ? "PASS" : "FAIL"));
        }

        builder.AppendLine();
        builder.AppendLine($"Overall: {scorecard.PassedCount}/{scorecard.TotalCount} passed");

        return builder.ToString();
    }

    private static string Symbol(bool value) => value ? "OK" : "FAIL";
}
