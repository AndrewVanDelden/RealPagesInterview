using System.Diagnostics;
using System.Globalization;
using Agent.Cli.Logging;
using Agent.Common;
using Agent.Composition;
using Agent.Decisions;
using Agent.Domain;
using Agent.Evaluation;
using Agent.Ingest;
using Agent.Orchestration;
using Agent.Safety;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Agent.Cli;

public static class CliExitCodes
{
    public const int Success = 0;
    public const int UsageError = 1;
    public const int PartialFailure = 2;
}

// Thin shell over the library (DESIGN.md section 5): parses arguments, wires the
// composition root, and runs the per-record batch loop. Holds no business rules
// of its own - every decision stays inside Agent library components.
// composerOverride is the fault-injection seam for the batch loop's per-record isolation:
// no input can make a record throw any more, so a test supplies a composer that does.
// judgeOverride is the same seam for --judge: BuildJudge always reaches for a real
// OpenAiCompletionClient, so a test driving --judge through a non-empty batch supplies its
// own judge rather than spending a real network call.
public sealed class CliRunner(
    IConfiguration configuration,
    TextWriter output,
    TextWriter error,
    IMessageComposer? composerOverride = null,
    SemanticJudge? judgeOverride = null)
{
    private static readonly HttpClient SharedHttpClient = new();

    // Playbook step 31: the judge model is pinned, not configured. A grade only means
    // something next to yesterday's grade if the same model gave both, and no second known
    // value has earned a setting here (step 41). The composer's model stays configurable
    // because a run may legitimately want to compare models; the instrument may not.
    private const string JudgeModel = "gpt-4o";

    // D37: how many records the batch loop runs at once. A constant, not a flag: no second
    // value has earned a setting (step 41). Four because D32 measured one model call at about
    // 1.5 to 4.5 s, so a sequential batch's wall clock is the sum of seconds per record, while
    // one record's calls run one after another, so four records put at most four requests in
    // flight against a per-minute vendor limit this project has never measured, and every 429
    // it returns now costs a retry and its backoff (D33). The template path spends well under
    // a millisecond a record, so the bound costs it nothing.
    private const int MaxConcurrentRecords = 4;

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        string? inputPath = GetOption(args, "--input");
        string? outputPath = GetOption(args, "--output");
        string composerName = GetOption(args, "--composer") ?? ComposerNames.Template;
        string? diagnosticsPath = GetOption(args, "--diagnostics");
        string? evalReportPath = GetOption(args, "--eval-report");
        string? logFilePath = GetOption(args, "--log-file");
        string? nowOption = GetOption(args, "--now");
        string? replayPath = GetOption(args, "--replay");
        string? reviewQueuePath = GetOption(args, "--review-queue");
        string? modelCallBudgetOption = GetOption(args, "--model-call-budget-ms");

        // D30: the judge is off unless it is asked for. It is a presence flag, not an
        // option with a value: there is one judge, and its model is pinned rather than
        // chosen per run (playbook step 31).
        bool judgeRequested = args.Contains("--judge", StringComparer.Ordinal);

        // D65: no argument of this program may be empty or all whitespace. A blank path
        // throws ArgumentException, which is not the IOException filter every open guard
        // here uses, so `--input ""`, `--output "   "`, `--log-file ""` and `--eval-report ""`
        // all ended the process unhandled. Closed here rather than by widening those filters,
        // which would ask a guard to swallow an exception a bug in the same try block could
        // also throw. One scan and no list of flags to keep in sync: every flag either takes
        // a value or is a presence flag, and no value any of them takes has a meaning when
        // blank. O(n) in the argument count.
        for (int index = 0; index < args.Length; index++)
        {
            if (!string.IsNullOrWhiteSpace(args[index]))
            {
                continue;
            }

            error.WriteLine(index == 0
                ? "Argument 1 is empty: no argument of this program may be empty."
                : $"The argument after '{args[index - 1]}' is empty: no argument of this program may be empty.");
            return CliExitCodes.UsageError;
        }

        if (inputPath is null || (outputPath is null && replayPath is null))
        {
            error.WriteLine("Usage: --input <file.jsonl> (--output <file.json> | --replay <file.json>) [--now <ISO-8601 date-time>] [--composer template|openai] [--model-call-budget-ms <n>] [--judge] [--diagnostics <file.json>] [--review-queue <file.json>] [--eval-report <file.txt>] [--log-file <file.log>]");
            return CliExitCodes.UsageError;
        }

        if (outputPath is not null && replayPath is not null)
        {
            error.WriteLine("--output and --replay are mutually exclusive: pass exactly one.");
            return CliExitCodes.UsageError;
        }

        // D43 and D14: a replay scores an output file that already exists and runs no
        // validator, so it has nothing to queue. An empty file from a replay would read as a
        // clean run rather than as a question that was never asked.
        if (reviewQueuePath is not null && replayPath is not null)
        {
            error.WriteLine("--review-queue needs a run that validates: it cannot be combined with --replay.");
            return CliExitCodes.UsageError;
        }

        // D10: the run's reference time is a value passed in, never a clock read inside the
        // library. Without the flag it is the current UTC time, so send times are relative
        // to today; the documented run against holdout_12.jsonl passes the oracle's date.
        DateTimeOffset referenceTime = DateTimeOffset.UtcNow;
        if (nowOption is not null && !DateTimeOffset.TryParse(nowOption, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out referenceTime))
        {
            error.WriteLine($"--now '{nowOption}' is not an ISO-8601 date-time (for example 2025-12-09T00:00:00-06:00).");
            return CliExitCodes.UsageError;
        }

        // D70: an evaluation run's own budget for the composer's model calls, in place of the one
        // D28 derives from the records. A positive whole number of milliseconds, and only on the
        // composer that makes model calls: anywhere else it would bound nothing, and a flag that
        // silently does nothing is a flag someone trusts.
        TimeSpan? modelCallBudget = null;
        if (modelCallBudgetOption is not null)
        {
            if (!int.TryParse(modelCallBudgetOption, NumberStyles.None, CultureInfo.InvariantCulture, out int budgetMs) || budgetMs <= 0)
            {
                error.WriteLine($"--model-call-budget-ms '{modelCallBudgetOption}' is not a positive whole number of milliseconds.");
                return CliExitCodes.UsageError;
            }

            if (composerName != ComposerNames.OpenAi)
            {
                error.WriteLine("--model-call-budget-ms applies only to --composer openai.");
                return CliExitCodes.UsageError;
            }

            modelCallBudget = TimeSpan.FromMilliseconds(budgetMs);
        }

        // FileLoggerProvider is disposed here, explicitly, rather than trusted to
        // LoggerFactory's own Dispose: an ILoggerProvider instance handed to AddProvider
        // is not one the DI container underneath LoggerFactory.Create constructed itself,
        // and is therefore not reliably disposed alongside it - a real leak this project
        // hit first as a locked log file in its own tests, not as a hypothetical.
        // There is no ILogger yet at this point in the run, so the failure goes straight to
        // error rather than through ReportFailure.
        Result<FileLoggerProvider?> fileLoggerOpen = TryOpenOptional("--log-file", logFilePath, path => new FileLoggerProvider(path));
        if (!fileLoggerOpen.IsSuccess)
        {
            error.WriteLine(fileLoggerOpen.Error);
            return CliExitCodes.UsageError;
        }

        FileLoggerProvider? fileLoggerProvider = fileLoggerOpen.Value;
        using FileLoggerProvider? disposableFileLoggerProvider = fileLoggerProvider;

        // AgentLog.Configure covers the whole run, including the JsonlRecordReader parse
        // below - LenientExpectedOutcomeConverter has no constructor-injection path of its
        // own (see AgentLog's own remarks) and reaches a logger only through this.
        using ILoggerFactory loggerFactory = BuildLoggerFactory(error, fileLoggerProvider);
        using IDisposable agentLogScope = AgentLog.Configure(loggerFactory);
        ILogger<CliRunner> log = loggerFactory.CreateLogger<CliRunner>();

        // D30: the judge is configuration, not a per-record decision, so it is built before
        // either path runs and a missing key is a usage error before any work happens
        // (playbook step 77).
        SemanticJudge? judge;
        try
        {
            judge = judgeRequested ? judgeOverride ?? BuildJudge(configuration, loggerFactory) : null;
        }
        catch (InvalidOperationException ex)
        {
            log.LogError(ex, "Judge selection failed.");
            error.WriteLine(ex.ToDiagnosticString());
            return CliExitCodes.UsageError;
        }

        if (replayPath is not null)
        {
            return await ReplayAsync(inputPath, replayPath, evalReportPath, judge, loggerFactory, log, cancellationToken);
        }

        // D65: the input open is guarded here, where ReadInput was already called, which is
        // before the composer is built - so a path that will not open still costs no time and
        // no money (playbook step 77).
        Result<StreamReader> inputOpen = OpenInputReader("--input", inputPath);
        if (ReportIfFailed(inputOpen, log))
        {
            return CliExitCodes.UsageError;
        }

        List<ProspectCase> cases;
        List<string> parseFailures;
        using (StreamReader inputReader = inputOpen.Value)
        {
            (cases, parseFailures) = ReadInput(inputReader, log);
        }

        int failureCount = parseFailures.Count;

        // D71: every input the batch cannot process is a row of the scorecard, starting with the
        // lines that did not parse; a record that throws in the loop below adds its own.
        List<RecordScore> unprocessedRows = parseFailures.Select(RecordScore.DidNotParse).ToList();

        // D28: the model call is bounded by the strictest latency budget the batch states, so
        // the composer is built after the records are read. Nothing that costs time or money
        // has happened yet (playbook step 77): reading the file is local, and a bad composer
        // name or a missing key still returns a usage error before the first call.
        var templateFallback = new TemplateMessageComposer();
        IMessageComposer baseComposer;
        try
        {
            baseComposer = composerOverride ?? composerName switch
            {
                ComposerNames.Template => templateFallback,
                ComposerNames.OpenAi => BuildOpenAiComposer(configuration, loggerFactory, ModelCallBudget.PerCallBudget(cases, modelCallBudget)),
                _ => throw new ArgumentException(
                    $"Unknown composer '{composerName}'. Expected '{ComposerNames.Template}' or '{ComposerNames.OpenAi}'."),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            log.LogError(ex, "Composer selection failed.");
            error.WriteLine(ex.ToDiagnosticString());
            return CliExitCodes.UsageError;
        }

        // One validator, deliberately shared by the compose-validate loop and the agent's own
        // step 5 gate. D48 has the loop hand a refused draft out for the agent to validate
        // again, and the agent's verdict is the one that decides whether it ships, so two
        // different validators here would be two answers to one question and a draft the loop
        // refused could go out. Sharing the instance is what makes that impossible.
        var safetyValidator = new SafetyValidator();
        IMessageComposer composer = new ValidatingMessageComposer(
            baseComposer,
            safetyValidator,
            templateFallback,
            loggerFactory.CreateLogger<ValidatingMessageComposer>());

        var agent = new LeasingMessageAgent(
            new ChannelSelector(),
            composer,
            safetyValidator,
            new SendScheduler(),
            new NextActionPlanner(),
            loggerFactory.CreateLogger<LeasingMessageAgent>());

        // Fixed before the loop below can add processing failures onto the same
        // failureCount: a record that throws stays in `cases` (per-record isolation), so
        // cases.Count + failureCount after the loop would count it twice.
        int recordsRead = cases.Count + failureCount;

        // Output streams are opened here, before the batch loop, deliberately: an invalid
        // output/diagnostics path (bad directory, no write permission) must fail immediately,
        // not after every record has already run through the composer and any LLM calls.
        // D64 gives each of the three the guard --log-file already has, so an unwritable path
        // is one stderr line naming its own flag and exit code 1 (playbook steps 77 and 79)
        // rather than an unhandled exception and an exit code that is none of the documented
        // three. A stream already opened is disposed by its own `await using` on the way out.
        // outputPath is non-null here: the usage check above requires it when there is no
        // --replay, and the replay path has already returned, so the open cannot return null.
        Result<StreamWriter?> outputOpen = OpenOutputStream("--output", outputPath);
        if (ReportIfFailed(outputOpen, log))
        {
            return CliExitCodes.UsageError;
        }

        await using StreamWriter outputStream = outputOpen.Value!;

        Result<StreamWriter?> diagnosticsOpen = OpenOutputStream("--diagnostics", diagnosticsPath);
        if (ReportIfFailed(diagnosticsOpen, log))
        {
            return CliExitCodes.UsageError;
        }

        await using StreamWriter? diagnosticsStream = diagnosticsOpen.Value;

        Result<StreamWriter?> reviewQueueOpen = OpenOutputStream("--review-queue", reviewQueuePath);
        if (ReportIfFailed(reviewQueueOpen, log))
        {
            return CliExitCodes.UsageError;
        }

        await using StreamWriter? reviewQueueStream = reviewQueueOpen.Value;

        log.LogInformation("Reference time for this run: {ReferenceTime}.", referenceTime.ToString("O", CultureInfo.InvariantCulture));

        var outputs = new List<AgentOutput>();
        var diagnosticsRecords = new List<TaskDiagnostics>();
        var reviewQueue = new List<ReviewQueueEntry>();
        var scoredRuns = new List<ScoredRun>();

        // D61, per batch: one wall-clock elapsed around the record loop, reported on the two
        // artifacts that are already per batch and never as a row in the diagnostics array.
        Stopwatch batchStopwatch = Stopwatch.StartNew();

        // D62, per batch: the same arrangement for tokens. Summed off the rows this loop writes,
        // so the number the scorecard prints is the number a reader adding up the diagnostics
        // file's model_cost column gets, and null while no record has gone near a model.
        ModelCostNotes? batchModelCost = null;

        // D37: records run under bounded concurrency, at most MaxConcurrentRecords at once,
        // because with a model in the path each costs seconds and a sequential batch's wall clock
        // is their sum. O(n) in the batch size: one agent run per record, at most
        // MaxConcurrentRecords in flight, and O(n) space for the runs held until the fold below.
        // Nothing in the template composer path observes cancellationToken itself, so the loop
        // does: the check here means a cancelled run starts no record, and ParallelOptions stops
        // starting records once the token is cancelled, after which ForEachAsync throws when the
        // records already in flight have finished.
        cancellationToken.ThrowIfCancellationRequested();
        var recordRuns = new RecordRun[cases.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, cases.Count),
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentRecords, CancellationToken = cancellationToken },
            async (index, recordCancellationToken) => recordRuns[index] = await RunRecordAsync(agent, cases[index], referenceTime, log, recordCancellationToken));

        // D37 and D14: folded in input order, whatever order the records finished in, because
        // --replay pairs output rows with input records by position, and the diagnostics, the
        // review queue, the scorecard and stderr's failure lines follow the same order. One
        // thread, so the lists, the failure count, the batch total and stderr need no lock.
        foreach (RecordRun run in recordRuns)
        {
            if (run is RecordRun.Failed failed)
            {
                failureCount++;
                error.WriteLine($"Record '{failed.Case.TaskId}' failed: {failed.Exception.ToDiagnosticString()}");

                // D71: the scorecard is a file a person keeps, so its row carries the exception
                // type alone (D46): nothing here knows who wrote the exception's message.
                unprocessedRows.Add(RecordScore.Unscoreable(failed.Case.TaskId, $"Record failed: {failed.Exception.ToRedactedDiagnosticString()}"));
                continue;
            }

            RecordRun.Completed completed = (RecordRun.Completed)run;
            AgentRunResult result = completed.Result;
            outputs.Add(result.Output);
            diagnosticsRecords.Add(new TaskDiagnostics(completed.Case.TaskId, result.Diagnostics, completed.IngestNotes, completed.LatencyMs));
            batchModelCost = ModelCostNotes.Add(batchModelCost, result.Diagnostics.ModelCost);

            // D43: one row per record the safety gate suppressed, and nothing else. A record
            // with no consented channel is not here (not contactable is the correct decision,
            // not a failure) and neither is a composition that produced no draft (there is
            // nothing to review), which is exactly what a null RejectedDraft says.
            if (result.RejectedDraft is { } rejectedDraft)
            {
                reviewQueue.Add(new ReviewQueueEntry(completed.Case.TaskId, rejectedDraft.Violations, rejectedDraft.Message));
            }

            scoredRuns.Add(new ScoredRun(completed.Case, result.Output, result.Diagnostics.SafetyViolationCount, completed.LatencyMs));
        }

        batchStopwatch.Stop();
        double batchLatencyMs = batchStopwatch.Elapsed.TotalMilliseconds;

        log.LogInformation(
            "Batch complete: {Total} record(s), {Failures} failure(s), {ElapsedMs}ms elapsed, model cost {ModelCost}.",
            recordsRead,
            failureCount,
            batchLatencyMs,
            ModelCostNotes.Describe(batchModelCost));

        var outputWriter = new JsonArrayRecordWriter<AgentOutput>();
        await outputWriter.WriteAllAsync(outputStream, outputs, cancellationToken);

        if (diagnosticsStream is not null)
        {
            var diagnosticsWriter = new JsonArrayRecordWriter<TaskDiagnostics>();
            await diagnosticsWriter.WriteAllAsync(diagnosticsStream, diagnosticsRecords, cancellationToken);
        }

        // Written even when the queue is empty (D43): a missing file cannot be told apart
        // from a flag nobody passed, and an empty queue is the number step 71 asks for.
        if (reviewQueueStream is not null)
        {
            var reviewQueueWriter = new JsonArrayRecordWriter<ReviewQueueEntry>();
            await reviewQueueWriter.WriteAllAsync(reviewQueueStream, reviewQueue, cancellationToken);
        }

        if (evalReportPath is not null)
        {
            // Scores the results already captured above - never re-runs the agent, so the
            // report describes exactly what was persisted to --output, not a second,
            // possibly different sample (this matters for non-deterministic composers).
            Scorecard scorecard = await ScoreAsync(scoredRuns, judge, loggerFactory, batchLatencyMs, batchModelCost, unprocessedRows, cancellationToken);
            if (!await WriteScorecardAsync(scorecard, evalReportPath, log, cancellationToken))
            {
                return CliExitCodes.UsageError;
            }
        }

        // A queued record leaves at exit 0 (D43): suppression is a correct pipeline outcome
        // and failureCount counts records the pipeline could not process at all.
        return failureCount == 0 ? CliExitCodes.Success : CliExitCodes.PartialFailure;
    }

    // D37: one record's run, from its log scope to its result, returned rather than written
    // anywhere shared, because up to MaxConcurrentRecords of these run at once. D16: the TaskId
    // scope is opened here, in CliRunner and nowhere else, inside this record's own async flow;
    // the scope stack LoggerFactory hands both providers follows that flow, so a line one
    // record logs never carries another's TaskId while both run, which
    // RunAsync_RecordsFailWhileOthersAreInFlight_EachFailureCarriesItsOwnTaskId proves.
    private static async Task<RecordRun> RunRecordAsync(
        LeasingMessageAgent agent,
        ProspectCase prospectCase,
        DateTimeOffset referenceTime,
        ILogger<CliRunner> log,
        CancellationToken cancellationToken)
    {
        using IDisposable? scope = log.BeginScope(new Dictionary<string, object> { [LogKeys.TaskId] = prospectCase.TaskId });

        // D1: one line per record naming every defaulted decision input and how many
        // members the record types do not declare, before any decision reads them.
        // The defaulted paths are this program's own schema names and are safe to log;
        // an unknown member's name is not (step 68), because a record chose it. The
        // count is the fact an operator reads this line for, and --diagnostics, which
        // is a file a person opens rather than a log stream, still carries every name.
        IngestNotes ingestNotes = IngestNotes.Describe(prospectCase);
        log.LogInformation(
            "Ingest: defaulted=[{DefaultedFields}] unknown={UnknownMemberCount} member(s).",
            string.Join(", ", ingestNotes.DefaultedFields),
            ingestNotes.UnknownMembers.Count);

        Stopwatch stopwatch = Stopwatch.StartNew();
        AgentRunResult result;
        try
        {
            result = await agent.RunAsync(prospectCase, referenceTime, cancellationToken);
        }
        catch (Exception ex)
        {
            // Per-record isolation: a bug that throws on one record must not discard the
            // output already produced for every other record in the batch. Every input
            // shape has a default (D1), so only a bug reaches here. The log entry is written
            // here, inside the record's scope; the stderr line and the scorecard row are the
            // fold's, in input order.
            log.LogError(ex, "Record failed.");
            return new RecordRun.Failed(prospectCase, ex);
        }

        stopwatch.Stop();

        // D61 option (c): one measurement, three readers. The log line, the diagnostics row
        // and the scored run are handed this one variable, so no two of them can state a
        // different latency for the same record.
        double latencyMs = stopwatch.Elapsed.TotalMilliseconds;

        log.LogInformation("Record processed in {ElapsedMs}ms.", latencyMs);
        return new RecordRun.Completed(prospectCase, ingestNotes, result, latencyMs);
    }

    // D14: re-score an existing output file against --input without running the agent.
    // Rows pair with records by position; a safety count and a latency exist only in the
    // run that wrote the file, so both score as not measured.
    private async Task<int> ReplayAsync(
        string inputPath,
        string replayPath,
        string? evalReportPath,
        SemanticJudge? judge,
        ILoggerFactory loggerFactory,
        ILogger<CliRunner> log,
        CancellationToken cancellationToken)
    {
        // D65: both reader paths get the guard, in the order they are read.
        Result<StreamReader> inputOpen = OpenInputReader("--input", inputPath);
        if (ReportIfFailed(inputOpen, log))
        {
            return CliExitCodes.UsageError;
        }

        List<ProspectCase> cases;
        List<string> parseFailures;
        using (StreamReader inputReader = inputOpen.Value)
        {
            (cases, parseFailures) = ReadInput(inputReader, log);
        }

        int failureCount = parseFailures.Count;

        Result<StreamReader> replayOpen = OpenInputReader("--replay", replayPath);
        if (ReportIfFailed(replayOpen, log))
        {
            return CliExitCodes.UsageError;
        }

        Result<IReadOnlyList<AgentOutput>> outputs;
        using (StreamReader replayReader = replayOpen.Value)
        {
            outputs = new JsonArrayRecordReader<AgentOutput>().ReadAll(replayReader);
        }

        Result<IReadOnlyList<ScoredRun>> aligned = outputs.IsSuccess
            ? ReplayAlignment.Align(cases, outputs.Value)
            : Result<IReadOnlyList<ScoredRun>>.Failure(outputs.Error);

        if (!aligned.IsSuccess)
        {
            ReportFailure(log, LogLevel.Error, $"--replay '{replayPath}': {aligned.Error}");
            return CliExitCodes.UsageError;
        }

        log.LogInformation("Replay: scoring {Count} output(s) from the file; safety and latency are not measured.", aligned.Value.Count);
        Scorecard scorecard = await ScoreAsync(aligned.Value, judge, loggerFactory, batchLatencyMs: null, batchModelCost: null, parseFailures.Select(RecordScore.DidNotParse).ToList(), cancellationToken);
        if (!await WriteScorecardAsync(scorecard, evalReportPath, log, cancellationToken))
        {
            return CliExitCodes.UsageError;
        }

        return failureCount == 0 ? CliExitCodes.Success : CliExitCodes.PartialFailure;
    }

    // A line that did not parse is one failure row with its line number; there is no task
    // id to scope the log line on, so the line number is the only identity it has. The
    // failure text is returned as well as reported, because the scorecard carries it as a
    // row (D71).
    // O(n) in the file size: one line parsed, and on failure reported, per iteration.
    private (List<ProspectCase> Cases, List<string> ParseFailures) ReadInput(StreamReader inputReader, ILogger<CliRunner> log)
    {
        IReadOnlyList<Result<ProspectCase>> readResults = new JsonlRecordReader().ReadAll(inputReader);

        var cases = new List<ProspectCase>(readResults.Count);
        var parseFailures = new List<string>();

        foreach (Result<ProspectCase> readResult in readResults)
        {
            if (readResult.IsSuccess)
            {
                cases.Add(readResult.Value);
                continue;
            }

            parseFailures.Add(readResult.Error);
            ReportFailure(log, LogLevel.Error, $"Record failed to parse: {readResult.Error}");
        }

        return (cases, parseFailures);
    }

    // A case missing its labeled expected outcome shows up as an unscoreable row rather
    // than aborting the whole report. The report always goes to the console; the file is
    // optional. O(n) in the batch size: one record's scoring error reported per iteration,
    // plus one file write.
    // Returns false only when --eval-report was passed and could not be written (D64). The
    // console report is already out by then, so the caller turns that into exit code 1 and
    // nothing else: the batch's own files are written and correct.
    private async Task<bool> WriteScorecardAsync(Scorecard scorecard, string? evalReportPath, ILogger<CliRunner> log, CancellationToken cancellationToken)
    {
        foreach (RecordScore score in scorecard.RecordScores)
        {
            if (score.ScoringError is not null)
            {
                ReportFailure(log, LogLevel.Warning, $"Eval: record '{score.TaskId}' could not be scored: {score.ScoringError}");
            }
        }

        string report = ScorecardFormatter.Format(scorecard);
        output.Write(report);

        if (evalReportPath is not null)
        {
            // D64: the same guard mechanism the three batch output streams get, at the write
            // instead of before the loop. The report is a report on the batch, so there is no
            // earlier moment at which it could be written.
            Result<bool> written = await TryPerformAsync("--eval-report", evalReportPath, () => File.WriteAllTextAsync(evalReportPath, report, cancellationToken));
            if (!written.IsSuccess)
            {
                ReportFailure(log, LogLevel.Error, written.Error);
                return false;
            }
        }

        return true;
    }

    // The log line and the stderr line always carry the same text: one failure, reported
    // through both channels, never independently worded.
    private void ReportFailure(ILogger log, LogLevel level, string message)
    {
        log.Log(level, message);
        error.WriteLine(message);
    }

    // D64/D65, generalized after the Claude Code review of PR #26 found the filter and the
    // message template independently restated at four call sites (--log-file, the three
    // output streams, and --eval-report's write): one exception-to-Result mechanism for every
    // path this program opens, shared instead of copied. A path the caller can fix (missing
    // directory, no permission, a locked or full volume) is an expected failure, and anything
    // else stays a bug and keeps throwing.
    private static Result<T> TryOpen<T>(string flag, string path, Func<string, T> open)
    {
        try
        {
            return Result<T>.Success(open(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<T>.Failure($"Could not open {flag} '{path}': {ex.ToDiagnosticString()}");
        }
    }

    // The same mechanism for a flag whose absence is success, not failure: a null path is the
    // caller not passing the flag at all, which carries no stream rather than an error.
    private static Result<T?> TryOpenOptional<T>(string flag, string? path, Func<string, T> open)
        where T : class
    {
        if (path is null)
        {
            return Result<T?>.Success(null);
        }

        Result<T> opened = TryOpen(flag, path, open);
        return opened.IsSuccess ? Result<T?>.Success(opened.Value) : Result<T?>.Failure(opened.Error);
    }

    // TryOpen's write-shaped sibling: an operation with no resource to hand back, just success
    // or the same translated failure. --eval-report's write goes through this rather than a
    // stream held open across the batch (D64's own distinction between the two), sharing the
    // filter and the message wording instead of restating them.
    private static async Task<Result<bool>> TryPerformAsync(string flag, string path, Func<Task> action)
    {
        try
        {
            await action();
            return Result<bool>.Success(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<bool>.Failure($"Could not open {flag} '{path}': {ex.ToDiagnosticString()}");
        }
    }

    // Collapses the Result-unwrap-then-early-return shape this file used to write out by hand
    // at every open: reports the failure through the usual channel and tells the caller
    // whether to bail. Instance method, not static: it calls this.ReportFailure, which writes
    // to the instance's own error stream.
    private bool ReportIfFailed<T>(Result<T> result, ILogger log)
    {
        if (result.IsSuccess)
        {
            return false;
        }

        ReportFailure(log, LogLevel.Error, result.Error);
        return true;
    }

    // D64: one guard for every output file the batch writes, so all three fail the same way
    // and each names the flag the caller passed. A null path is the flag not being passed at
    // all, which is a success carrying no stream, not a failure.
    private static Result<StreamWriter?> OpenOutputStream(string flag, string? path) =>
        TryOpenOptional(flag, path, p => new StreamWriter(p));

    // D65: the mirror of OpenOutputStream for the two paths a run reads, --input and --replay.
    // Same filter and same wording deliberately: a path the caller can fix is one class of
    // failure whichever direction the bytes go, and a wider filter here than there would make
    // one program say two things about one operating-system fact. Exit code 1 rather than 2 is
    // the caller's half of that: 2 means some records were processed and some were not, and a
    // file that never opened has no records at all. No null path to answer for, unlike the
    // output flags: both callers reach this only with a path the usage check required.
    private static Result<StreamReader> OpenInputReader(string flag, string path) =>
        TryOpen(flag, path, p => new StreamReader(p));

    private static string? GetOption(string[] cliArgs, string name)
    {
        int index = Array.IndexOf(cliArgs, name);
        return index >= 0 && index + 1 < cliArgs.Length ? cliArgs[index + 1] : null;
    }

    // D30: the judge model is pinned separately from the composer's, so the two are not the
    // same model even though one vendor key makes them the same family. Its calls are
    // evaluation, not the product's per-record work, so they are not bounded by the batch's
    // latency budget the way a compose call is (D28).
    private static SemanticJudge BuildJudge(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        string apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException(
                "--judge needs OpenAI:ApiKey. Set it with: dotnet user-secrets set \"OpenAI:ApiKey\" \"<key>\" --project src/Agent.Cli");
        var completionClient = new OpenAiCompletionClient(SharedHttpClient, apiKey, JudgeModel);
        return new SemanticJudge(completionClient, loggerFactory.CreateLogger<SemanticJudge>());
    }

    // The scorecard, and the judge's two verdicts on top of it when the run asked for them.
    // Both paths score the same way; the judge is one signal beside the deterministic checks
    // and never replaces one (D30).
    // Both batch numbers are null on the replay path (D14): no record loop ran there, so nothing
    // was timed and nothing was spent, and the scorecard says so rather than printing zeros.
    private static async Task<Scorecard> ScoreAsync(
        IReadOnlyList<ScoredRun> runs,
        SemanticJudge? judge,
        ILoggerFactory loggerFactory,
        double? batchLatencyMs,
        ModelCostNotes? batchModelCost,
        IReadOnlyList<RecordScore> unprocessedRows,
        CancellationToken cancellationToken)
    {
        var evaluator = new Evaluator(loggerFactory.CreateLogger<Evaluator>());
        Scorecard scorecard = evaluator.Evaluate(runs, batchLatencyMs, batchModelCost);
        Scorecard judged = judge is null ? scorecard : await judge.JudgeAsync(scorecard, runs, cancellationToken);

        // D71: appended after the judge, which pairs rows with runs by position.
        return judged.AppendUnprocessed(unprocessedRows);
    }

    private static IMessageComposer BuildOpenAiComposer(IConfiguration configuration, ILoggerFactory loggerFactory, TimeSpan? callTimeout)
    {
        string apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException(
                "OpenAI:ApiKey is not configured. Set it with: dotnet user-secrets set \"OpenAI:ApiKey\" \"<key>\" --project src/Agent.Cli");
        string model = configuration["OpenAI:Model"] ?? "gpt-4o-mini";

        // Playbook step 78: the log states the inputs a decision used, and which model wrote a
        // run's prose is one; the key is never logged, the model name is configuration.
        loggerFactory.CreateLogger<CliRunner>().LogInformation("Composer: openai, model {Model}.", model);

        var completionClient = new OpenAiCompletionClient(SharedHttpClient, apiKey, model, callTimeout);
        return new OpenAiMessageComposer(completionClient, loggerFactory.CreateLogger<OpenAiMessageComposer>());
    }

    // Console goes through ConsoleLoggerProvider(error), not Microsoft.Extensions.Logging.Console's
    // AddConsole: AddConsole is hardwired to the real Console.Out/Error and can't be
    // redirected, which would both collide with --eval-report's own stdout output and
    // defeat every test's attempt to isolate itself via injected StringWriters (see
    // ConsoleLoggerProvider's own remarks). Scopes carry a record's TaskId (see
    // LeasingMessageAgent.RunAsync and Evaluator.Evaluate) onto every line emitted while
    // processing it. --log-file additionally persists the same lines to a real file via
    // FileLoggerProvider - the "a log file" gap the Sprint 8 audit flagged as missing from
    // this codebase entirely (TalkingPoints.md history before 2026-09-06).
    private static ILoggerFactory BuildLoggerFactory(TextWriter error, ILoggerProvider? fileLoggerProvider) =>
        LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new ConsoleLoggerProvider(error));

            if (fileLoggerProvider is not null)
            {
                builder.AddProvider(fileLoggerProvider);
            }
        });
}
