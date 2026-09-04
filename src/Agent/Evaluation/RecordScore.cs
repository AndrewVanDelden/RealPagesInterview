namespace Agent.Evaluation;

public sealed record RecordScore(
    string TaskId,
    bool ChannelMatches,
    bool NextActionTypeMatches,
    bool OptOutPresent,
    bool PrimaryCtaPresent,
    bool SafetyViolationsWithinBudget,
    double PersonalizationScore,
    bool PersonalizationScoreMet,
    double LatencyMs,
    bool LatencyWithinBudget)
{
    public bool Passed =>
        ChannelMatches && NextActionTypeMatches && OptOutPresent && PrimaryCtaPresent &&
        SafetyViolationsWithinBudget && PersonalizationScoreMet && LatencyWithinBudget;
}
