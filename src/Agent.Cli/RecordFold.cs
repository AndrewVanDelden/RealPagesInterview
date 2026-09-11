using Agent.Common;
using Agent.Composition;
using Agent.Domain;
using Agent.Evaluation;
using Agent.Ingest;
using Agent.Orchestration;

namespace Agent.Cli;

// The batch's fold: takes each record's run in input order, once every earlier record has
// finished, and writes its rows to the batch files at once rather than holding them to the end.
// Input order is kept because --replay pairs output rows with input records by position, and
// the other files and stderr's failure lines follow the same order. One caller, one run at a
// time, so the counts, the cost total and the score rows need no lock.
internal sealed class RecordFold(
    TextWriter error,
    JsonArrayRecordWriter<AgentOutput> outputRows,
    JsonArrayRecordWriter<TaskDiagnostics>? diagnosticsRows,
    JsonArrayRecordWriter<ReviewQueueEntry>? reviewQueueRows,
    Evaluator? evaluator,
    bool keepRunsForJudge)
{
    private readonly List<RecordScore> failedRecordRows = [];
    private readonly List<RecordScore> scores = [];
    private readonly List<ScoredRun> judgedRuns = [];
    private int? latencyBudgetMs;

    public int FailedRecordCount => failedRecordRows.Count;

    // Summed off the rows the fold writes, so the total the scorecard prints is what adding up
    // the diagnostics file's model_cost column gives, and null while no record called a model.
    public ModelCostNotes? BatchModelCost { get; private set; }

    // One scorecard row per record that threw, carrying the exception type alone: the scorecard
    // is a file a person keeps, and nothing here knows who wrote the exception's message.
    public IReadOnlyList<RecordScore> FailedRecordRows => failedRecordRows;

    // With --judge, every scored run, because the judge grades the whole scorecard after the
    // batch and pairs its rows with these runs by position. Empty otherwise.
    public IReadOnlyList<ScoredRun> JudgedRuns => judgedRuns;

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        await outputRows.BeginAsync(cancellationToken);

        if (diagnosticsRows is not null)
        {
            await diagnosticsRows.BeginAsync(cancellationToken);
        }

        if (reviewQueueRows is not null)
        {
            await reviewQueueRows.BeginAsync(cancellationToken);
        }
    }

    // Every file is closed even when it got no rows: an empty review queue is the number a
    // healthy run reports, and a missing file cannot be told apart from a flag nobody passed.
    public async Task EndAsync(CancellationToken cancellationToken)
    {
        await outputRows.EndAsync(cancellationToken);

        if (diagnosticsRows is not null)
        {
            await diagnosticsRows.EndAsync(cancellationToken);
        }

        if (reviewQueueRows is not null)
        {
            await reviewQueueRows.EndAsync(cancellationToken);
        }
    }

    // O(size of one record's rows): one row per file, and one scoring pass with --eval-report.
    public async Task AddAsync(RecordRun run, CancellationToken cancellationToken)
    {
        if (run is RecordRun.Failed failed)
        {
            error.WriteLine($"Record '{failed.Case.TaskId}' failed: {failed.Exception.ToDiagnosticString()}");
            failedRecordRows.Add(RecordScore.Unscoreable(failed.Case.TaskId, $"Record failed: {failed.Exception.ToRedactedDiagnosticString()}"));
            return;
        }

        RecordRun.Completed completed = (RecordRun.Completed)run;
        AgentRunResult result = completed.Result;
        await outputRows.WriteRowAsync(result.Output, cancellationToken);

        if (diagnosticsRows is not null)
        {
            await diagnosticsRows.WriteRowAsync(new TaskDiagnostics(completed.Case.TaskId, result.Diagnostics, completed.IngestNotes, completed.LatencyMs), cancellationToken);
        }

        BatchModelCost = ModelCostNotes.Add(BatchModelCost, result.Diagnostics.ModelCost);

        // One queue row per record the safety gate suppressed, and nothing else. A record with no
        // consented channel is not here (not contactable is the correct decision, not a failure),
        // and neither is a composition with no draft (nothing to review): a null RejectedDraft.
        if (reviewQueueRows is not null && result.RejectedDraft is { } rejectedDraft)
        {
            await reviewQueueRows.WriteRowAsync(new ReviewQueueEntry(completed.Case.TaskId, rejectedDraft.Violations, rejectedDraft.Message), cancellationToken);
        }

        if (evaluator is not null)
        {
            Score(evaluator, new ScoredRun(completed.Case, result.Output, result.Diagnostics.SafetyViolationCount, completed.LatencyMs));
        }
    }

    // Every scored record in input order, with the batch's own numbers. O(n log n) in the scored
    // records, for the p95 sort the scorecard does when it is constructed.
    public Scorecard ScoreBatch(double batchLatencyMs) => new(scores, latencyBudgetMs, batchLatencyMs, BatchModelCost);

    // One record scored as it is folded, by the rule a whole batch is scored by, so its row is
    // the row a whole-batch pass gives it. The budget the batch p95 is judged against is the
    // strictest any scored record states, and null when none states one. O(1) beyond the row.
    private void Score(Evaluator activeEvaluator, ScoredRun run)
    {
        scores.Add(activeEvaluator.ScoreRecord(run));
        latencyBudgetMs = Evaluator.StricterLatencyBudget(latencyBudgetMs, run);

        if (keepRunsForJudge)
        {
            judgedRuns.Add(run);
        }
    }
}
