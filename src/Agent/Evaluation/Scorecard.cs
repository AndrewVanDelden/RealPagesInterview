using Agent.Composition;

namespace Agent.Evaluation;

// The batch verdict. A class, not a record: the tallies and the p95 are computed once from the rows, so a copy
// that swapped the rows would report totals that disagree with them. With one constructor and
// no `with`, the only way to get different rows is a new scorecard, which recomputes both.
public sealed class Scorecard
{
    // Computed once at construction, not on every read: ScorecardFormatter reads
    // LatencyP95Ms and LatencyP95 (which reads LatencyP95Ms again) in the same report, and
    // PassedCountOf/MeasuredCountOf are each called once per check per report.
    private readonly double? latencyP95Ms;
    private readonly IReadOnlyDictionary<EvaluationCheck, (int Passed, int Measured)> tallies;

    public Scorecard(
        IReadOnlyList<RecordScore> recordScores,
        int? latencyBudgetMs,
        double? batchLatencyMs = null,
        ModelCostNotes? batchModelCost = null)
    {
        // Copied once, O(n): the caller's list may be one it keeps editing, and rows that
        // changed after the tallies were counted would disagree with them.
        RecordScores = Array.AsReadOnly(recordScores.ToArray());
        LatencyBudgetMs = latencyBudgetMs;
        BatchLatencyMs = batchLatencyMs;
        BatchModelCost = batchModelCost;
        latencyP95Ms = ComputeLatencyP95Ms(RecordScores);
        tallies = ComputeTallies(RecordScores);
    }

    public IReadOnlyList<RecordScore> RecordScores { get; }

    // The strictest p95_latency_ms any record states (a p95 under the strictest budget is under
    // every budget), null when none states one.
    public int? LatencyBudgetMs { get; }

    // One wall-clock elapsed around the whole record loop, not a sum or a percentile of the
    // per-record numbers; here rather than in the diagnostics file, which is one row per unit of
    // work. Null on a run that executed no record loop, which is --replay, where nothing was timed.
    public double? BatchLatencyMs { get; }

    // The same arrangement for tokens: the sum of the model cost on the diagnostics rows the
    // batch wrote, so a reader who adds that column up gets this number; null when no record on
    // the run went near a model.
    public ModelCostNotes? BatchModelCost { get; }

    public int TotalCount => RecordScores.Count;

    public int PassedCount => RecordScores.Count(score => score.Passed);

    public bool AllPassed => RecordScores.All(score => score.Passed) && LatencyP95 != CheckResult.Failed;

    public double? LatencyP95Ms => latencyP95Ms;

    public CheckResult LatencyP95 =>
        LatencyP95Ms is not { } p95 || LatencyBudgetMs is not { } budget
            ? CheckResult.NotMeasured
            : p95 <= budget ? CheckResult.Passed : CheckResult.Failed;

    // Every input the batch could not process, a line that did not parse or a record that
    // threw, as one unscoreable row each. Called after the judge, which pairs rows with runs by
    // position, so these rows sit past the last run.
    // O((n + m) log(n + m)) for the p95 sort and O(n + m) for the tallies, both redone in full
    // over `RecordScores` and `unprocessedRows` together: the appended rows are always
    // unmeasured (Unscoreable/DidNotParse rows carry a null LatencyMs and every check as
    // NotMeasured), so they cannot move either number, but the constructor is the one place
    // either number is computed, so the new scorecard pays for both.
    public Scorecard AppendUnprocessed(IReadOnlyList<RecordScore> unprocessedRows) =>
        new([.. RecordScores, .. unprocessedRows], LatencyBudgetMs, BatchLatencyMs, BatchModelCost);

    public int PassedCountOf(EvaluationCheck check) => tallies[check].Passed;

    public int MeasuredCountOf(EvaluationCheck check) => tallies[check].Measured;

    // Nearest-rank p95 over the records that carry a latency (A15, playbook step 32):
    // O(n log n) in the batch size for the sort. Integer rank arithmetic so 0.95 * n never
    // rounds across a whole number.
    private static double? ComputeLatencyP95Ms(IReadOnlyList<RecordScore> recordScores)
    {
        double[] sorted = recordScores.Select(score => score.LatencyMs).OfType<double>().Order().ToArray();

        if (sorted.Length == 0)
        {
            return null;
        }

        int rank = (95 * sorted.Length + 99) / 100;
        return sorted[rank - 1];
    }

    // O(n) in the batch size: one pass over every record, tallying all checks together
    // instead of one full scan per check.
    private static IReadOnlyDictionary<EvaluationCheck, (int Passed, int Measured)> ComputeTallies(IReadOnlyList<RecordScore> recordScores)
    {
        EvaluationCheck[] allChecks = EvaluationChecks.All;
        var tallies = allChecks.ToDictionary(check => check, _ => (Passed: 0, Measured: 0));

        foreach (RecordScore score in recordScores)
        {
            foreach (EvaluationCheck check in allChecks)
            {
                CheckResult result = score.ResultOf(check);

                if (result == CheckResult.NotMeasured)
                {
                    continue;
                }

                (int passed, int measured) = tallies[check];
                tallies[check] = (result == CheckResult.Passed ? passed + 1 : passed, measured + 1);
            }
        }

        return tallies;
    }
}
