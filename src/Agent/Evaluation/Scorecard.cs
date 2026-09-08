namespace Agent.Evaluation;

// The batch verdict. LatencyBudgetMs is the strictest p95_latency_ms any record states
// (a p95 under the strictest budget is under every budget), null when none states one.
public sealed record Scorecard(IReadOnlyList<RecordScore> RecordScores, int? LatencyBudgetMs)
{
    public int TotalCount => RecordScores.Count;

    public int PassedCount => RecordScores.Count(score => score.Passed);

    public bool AllPassed => RecordScores.All(score => score.Passed) && LatencyP95 != CheckResult.Failed;

    // Nearest-rank p95 over the records that carry a latency (A15, playbook step 32):
    // O(n log n) in the batch size for the sort. Integer rank arithmetic so 0.95 * n never
    // rounds across a whole number.
    public double? LatencyP95Ms
    {
        get
        {
            double[] sorted = RecordScores.Select(score => score.LatencyMs).OfType<double>().Order().ToArray();

            if (sorted.Length == 0)
            {
                return null;
            }

            int rank = (95 * sorted.Length + 99) / 100;
            return sorted[rank - 1];
        }
    }

    public CheckResult LatencyP95 =>
        LatencyP95Ms is not { } p95 || LatencyBudgetMs is not { } budget
            ? CheckResult.NotMeasured
            : p95 <= budget ? CheckResult.Passed : CheckResult.Failed;

    public int PassedCountOf(EvaluationCheck check) => RecordScores.Count(score => score.ResultOf(check) == CheckResult.Passed);

    public int MeasuredCountOf(EvaluationCheck check) => RecordScores.Count(score => score.ResultOf(check) != CheckResult.NotMeasured);
}
