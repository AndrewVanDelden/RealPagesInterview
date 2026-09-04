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
    bool LatencyWithinBudget,
    string? ScoringError = null)
{
    public bool Passed =>
        ScoringError is null &&
        ChannelMatches && NextActionTypeMatches && OptOutPresent && PrimaryCtaPresent &&
        SafetyViolationsWithinBudget && PersonalizationScoreMet && LatencyWithinBudget;

    // A case that could not be scored at all (e.g. missing its labeled expected outcome) -
    // distinct from a case that was scored and failed one or more checks. Keeps every
    // attempted case visible in the scorecard instead of aborting the whole batch.
    public static RecordScore Unscoreable(string taskId, string reason) =>
        new(taskId, false, false, false, false, false, 0.0, false, 0.0, false, reason);
}
