using Agent.Composition;

namespace Agent.Evaluation;

// The batch verdict. LatencyBudgetMs is the strictest p95_latency_ms any record states
// (a p95 under the strictest budget is under every budget), null when none states one.
//
// D61: BatchLatencyMs is one wall-clock elapsed around the whole record loop, not a sum or a
// percentile of the per-record numbers, and it lives here rather than in the diagnostics file
// because that file is one row per unit of work. Null on a run that executed no record loop,
// which is --replay (D14), where nothing was timed.
//
// D62: BatchModelCost is the same arrangement for tokens. It is the sum of the model cost on
// the diagnostics rows the batch wrote, so a reader who adds that column up gets this number,
// and it is null when no record on the run went near a model.
public sealed record Scorecard(
    IReadOnlyList<RecordScore> RecordScores,
    int? LatencyBudgetMs,
    double? BatchLatencyMs = null,
    ModelCostNotes? BatchModelCost = null)
{
    // Computed once at construction, so a `with` copy that replaces RecordScores would carry
    // these numbers unchanged: build a new Scorecard instead (SemanticJudge does).
    // Computed once at construction, not on every read: ScorecardFormatter reads
    // LatencyP95Ms and LatencyP95 (which reads LatencyP95Ms again) in the same report, and
    // PassedCountOf/MeasuredCountOf are each called once per check per report.
    private readonly double? latencyP95Ms = ComputeLatencyP95Ms(RecordScores);
    private readonly IReadOnlyDictionary<EvaluationCheck, (int Passed, int Measured)> tallies = ComputeTallies(RecordScores);

    public int TotalCount => RecordScores.Count;

    public int PassedCount => RecordScores.Count(score => score.Passed);

    public bool AllPassed => RecordScores.All(score => score.Passed) && LatencyP95 != CheckResult.Failed;

    public double? LatencyP95Ms => latencyP95Ms;

    public CheckResult LatencyP95 =>
        LatencyP95Ms is not { } p95 || LatencyBudgetMs is not { } budget
            ? CheckResult.NotMeasured
            : p95 <= budget ? CheckResult.Passed : CheckResult.Failed;

    // D71: every input the batch could not process, a line that did not parse or a record that
    // threw, as one unscoreable row each. Built rather than copied with `with`, because the
    // tallies and the p95 are computed at construction. Called after the judge, which pairs rows
    // with runs by position, so these rows sit past the last run.
    // O(n + m) in the rows already here and the rows appended.
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
