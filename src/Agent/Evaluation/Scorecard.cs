namespace Agent.Evaluation;

public sealed record Scorecard(IReadOnlyList<RecordScore> RecordScores)
{
    public int TotalCount => RecordScores.Count;

    public int PassedCount => RecordScores.Count(score => score.Passed);

    public bool AllPassed => RecordScores.All(score => score.Passed);
}
