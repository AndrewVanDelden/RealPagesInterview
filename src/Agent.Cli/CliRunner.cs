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
using Microsoft.Extensions.Logging.Abstractions;

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

    // Records log to error from their own threads while the fold writes its failure lines to the
    // same writer, so every write goes through one lock. Each log entry and each failure line is
    // one call, so lines interleave whole and never split.
    private readonly TextWriter error = TextWriter.Synchronized(error);

    // Playbook step 31: the judge model is pinned, not configured. A grade only means
    // something next to yesterday's grade if the same model gave both, and no second known
    // value has earned a setting here (step 41). The composer's model stays configurable
    // because a run may legitimately want to compare models; the instrument may not.
    private const string JudgeModel = "gpt-4o";

    // How many records the batch loop runs at once. A constant, not a flag: no second value
    // has earned a setting (step 41). One model call was measured at about 1.5 to 4.5 s and one
    // record's calls run one after another, so a sequential batch's wall clock is the sum of
    // seconds per record. Four puts at most four requests in flight against a per-minute vendor
    // limit this project has never measured, and every 429 it returns costs a retry and its
    // backoff, since a 429 is one of the transient statuses the client retries. The template
    // path spends well under a millisecond a record, so the bound costs it nothing.
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
        string? rulesPath = GetOption(args, "--rules");

        // The judge is off unless it is asked for, so an offline run and every pinned baseline
        // never depend on a network call. It is a presence flag, not an option with a value:
        // there is one judge, and its model is pinned rather than chosen per run (playbook
        // step 31).
        bool judgeRequested = args.Contains("--judge", StringComparer.Ordinal);

        // No argument of this program may be empty or all whitespace. A blank path throws
        // ArgumentException, which is not the IOException filter every open guard here uses, so
        // `--input ""`, `--output "   "`, `--log-file ""` or `--eval-report ""` would end the
        // process unhandled. Closed here rather than by widening those filters, which would ask a
        // guard to swallow an exception a bug in the same try block could also throw. One scan and
        // no list of flags to keep in sync: every flag either takes a value or is a presence flag,
        // and no value any of them takes has a meaning when blank. O(n) in the argument count.
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
            error.WriteLine("Usage: --input <file.jsonl> (--output <file.json> | --replay <file.json>) [--now <ISO-8601 date-time>] [--composer template|openai] [--model-call-budget-ms <n>] [--judge] [--rules <file.json>] [--diagnostics <file.json>] [--review-queue <file.json>] [--eval-report <file.txt>] [--log-file <file.log>]");
            return CliExitCodes.UsageError;
        }

        if (outputPath is not null && replayPath is not null)
        {
            error.WriteLine("--output and --replay are mutually exclusive: pass exactly one.");
            return CliExitCodes.UsageError;
        }

        // A replay scores an output file that already exists and runs no validator, so it has
        // nothing to queue. An empty file from a replay would read as a clean run rather than as
        // a question that was never asked.
        if (reviewQueuePath is not null && replayPath is not null)
        {
            error.WriteLine("--review-queue needs a run that validates: it cannot be combined with --replay.");
            return CliExitCodes.UsageError;
        }

        // A replay scores a file that already exists and runs no planner or scheduler, so a
        // rules file could change nothing it reports. Refused, so a replay never reads as
        // scored under rules it did not use.
        if (rulesPath is not null && replayPath is not null)
        {
            error.WriteLine("--rules needs a run that plans: it cannot be combined with --replay.");
            return CliExitCodes.UsageError;
        }

        // The run's reference time is a value passed in, never a clock read inside the library,
        // so a documented run can pass the date its labels were written against and reproduce
        // its send times on any day. Without the flag it is the current UTC time, so send times
        // are relative to today; the documented run against holdout_12.jsonl passes the oracle's
        // date.
        DateTimeOffset referenceTime = DateTimeOffset.UtcNow;
        if (nowOption is not null && !DateTimeOffset.TryParse(nowOption, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out referenceTime))
        {
            error.WriteLine($"--now '{nowOption}' is not an ISO-8601 date-time (for example 2025-12-09T00:00:00-06:00).");
            return CliExitCodes.UsageError;
        }

        // An evaluation run's own budget for the composer's model calls, in place of the one
        // derived from the strictest p95_latency_ms the records state, which few completions
        // measured so far fit, so without it most records fall back to the template. A positive
        // whole number of milliseconds, and only on the composer that makes model calls: anywhere
        // else it would bound nothing, and a flag that silently does nothing is a flag someone
        // trusts. The scorecard's p95 check still judges the run against the records' own budget.
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

        // The judge is configuration, not a per-record decision, so it is built before
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

        // The input is opened before the composer is built, so a path that will not open costs
        // no time and no money and exits 1. It stays open for the batch, which reads it a line
        // at a time.
        Result<StreamReader> inputOpen = OpenInputReader("--input", inputPath);
        if (ReportIfFailed(inputOpen, log))
        {
            return CliExitCodes.UsageError;
        }

        using StreamReader inputReader = inputOpen.Value;

        // The rules file replaces the compiled catalog and send slots whole. It is read and
        // checked before the composer is built and before any batch output opens, so a bad
        // file costs no record, no model call and no partial output. A refusal prints the
        // loader's own lines, one per bad row, and the success line gives row counts only.
        Result<DecisionRules?> rulesLoad = LoadRules(rulesPath);
        if (ReportIfFailed(rulesLoad, log))
        {
            return CliExitCodes.UsageError;
        }

        DecisionRules? rules = rulesLoad.Value;
        if (rules is not null)
        {
            log.LogInformation(
                "Rules file loaded: {CatalogRowCount} catalog row(s) beside the generic row, {SendSlotRowCount} send slot row(s).",
                rules.Catalog.Rows.Count,
                rules.SendSlots.Rows.Count);
        }

        // A model call is bounded by the strictest latency budget the records state, so the
        // model composer is built after a first pass over the file finds it. Nothing that costs
        // time or money has happened yet: reading the file is local, and a bad composer name or
        // a missing key still returns a usage error before the first call.
        var templateFallback = new TemplateMessageComposer();
        IMessageComposer baseComposer;
        try
        {
            baseComposer = composerOverride ?? composerName switch
            {
                ComposerNames.Template => templateFallback,
                ComposerNames.OpenAi => BuildOpenAiComposer(configuration, loggerFactory, ModelCallBudgetFromInput(inputReader, modelCallBudget)),
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
        // step 5 gate. The loop hands a refused draft out, so the review queue can show it, and
        // the agent validates it again; the agent's verdict is the one that decides whether it
        // ships, so two different validators here would be two answers to one question and a
        // draft the loop refused could go out. Sharing the instance is what makes that impossible.
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
            new SendScheduler(rules?.SendSlots),
            new NextActionPlanner(rules?.Catalog),
            loggerFactory.CreateLogger<LeasingMessageAgent>());

        // Output streams are opened here, before the batch loop, deliberately: an invalid
        // output, diagnostics or review-queue path (bad directory, no write permission) must fail
        // before any record runs through the composer and any model call. Each gets the guard
        // --log-file has, so an unwritable path is one stderr line naming its own flag and exit
        // code 1 (playbook steps 77 and 79), never an unhandled exception and an undocumented exit
        // code. A stream already opened is disposed by its own `await using` on the way out.
        // outputPath is non-null here: the usage check requires it when there is no --replay, and
        // the replay path has already returned, so the open cannot return null.
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

        // Each file gets a record's rows as the record is folded, so memory holds the window of
        // runs waiting on an earlier record, never the batch's rows. With --eval-report each
        // record is scored as it is folded and keeps one small score row, because the p95, the
        // tallies and the report cover the whole batch; with --judge the scored runs stay too.
        Evaluator? evaluator = evalReportPath is null ? null : new Evaluator(loggerFactory.CreateLogger<Evaluator>());
        var fold = new RecordFold(
            error,
            new JsonArrayRecordWriter<AgentOutput>(outputStream),
            diagnosticsStream is null ? null : new JsonArrayRecordWriter<TaskDiagnostics>(diagnosticsStream),
            reviewQueueStream is null ? null : new JsonArrayRecordWriter<ReviewQueueEntry>(reviewQueueStream),
            evaluator,
            keepRunsForJudge: judge is not null);

        // Per batch: one wall-clock elapsed around the record loop, reported on the two artifacts
        // that are already per batch, the log line and the scorecard, and never as a row in the
        // diagnostics array, which holds one row per record. The input is read and parsed, and
        // rows are written as records are folded, inside the loop, so it includes both.
        Stopwatch batchStopwatch = Stopwatch.StartNew();

        // Nothing in the template composer path observes cancellationToken itself, so the batch
        // does: a run cancelled before it starts writes nothing and starts no record.
        cancellationToken.ThrowIfCancellationRequested();
        await fold.BeginAsync(cancellationToken);
        (int recordsRead, List<string> parseFailures) = await RunWindowedAsync(
            agent, new JsonlRecordReader().ReadEach(inputReader), referenceTime, log, fold, cancellationToken);

        batchStopwatch.Stop();
        double batchLatencyMs = batchStopwatch.Elapsed.TotalMilliseconds;
        int failureCount = parseFailures.Count + fold.FailedRecordCount;

        log.LogInformation(
            "Batch complete: {Total} record(s), {Failures} failure(s), {ElapsedMs}ms elapsed, model cost {ModelCost}.",
            recordsRead,
            failureCount,
            batchLatencyMs,
            ModelCostNotes.Describe(fold.BatchModelCost));

        await fold.EndAsync(cancellationToken);

        if (evalReportPath is not null)
        {
            // Scores the results already written above and never re-runs the agent, so the
            // report describes exactly what was persisted to --output. Every input the batch
            // could not process is a row too: the lines that did not parse, then the records
            // that threw, each in input order.
            Scorecard scorecard = await JudgeAndAppendAsync(
                fold.ScoreBatch(batchLatencyMs),
                fold.JudgedRuns,
                judge,
                [.. parseFailures.Select(RecordScore.DidNotParse), .. fold.FailedRecordRows],
                cancellationToken);
            if (!await WriteScorecardAsync(scorecard, evalReportPath, log, cancellationToken))
            {
                return CliExitCodes.UsageError;
            }
        }

        // A queued record leaves at exit 0: suppression is a correct pipeline outcome, work for
        // a person rather than a broken batch, and failureCount counts records the pipeline could
        // not process at all.
        return failureCount == 0 ? CliExitCodes.Success : CliExitCodes.PartialFailure;
    }

    // Reads the input a line at a time and keeps at most MaxConcurrentRecords runs ahead of the
    // fold, which takes them in input order, so memory holds that window and never the batch; a
    // slow record at the head holds back later starts until it finishes. A line that did not
    // parse is reported once every earlier record is folded, so stderr keeps input order, and
    // its failure text is all that is kept of it, for the scorecard. On a cancel or a throw, the
    // records in flight are cancelled and awaited before the exception leaves.
    // O(n) time in the input lines; O(MaxConcurrentRecords + f) space, f the lines that did not parse.
    private async Task<(int RecordsRead, List<string> ParseFailures)> RunWindowedAsync(
        LeasingMessageAgent agent,
        IEnumerable<Result<ProspectCase>> reads,
        DateTimeOffset referenceTime,
        ILogger<CliRunner> log,
        RecordFold fold,
        CancellationToken cancellationToken)
    {
        using var inFlightCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var window = new Queue<Task<RecordRun>>(MaxConcurrentRecords);
        var parseFailures = new List<string>();
        int recordsRead = 0;
        try
        {
            foreach (Result<ProspectCase> read in reads)
            {
                // Every non-blank line is a record read, parsed or not; a record that later throws
                // is still one record, never two.
                recordsRead++;

                if (!read.IsSuccess)
                {
                    await FoldEveryRunAsync(window, fold, cancellationToken);
                    parseFailures.Add(read.Error);
                    ReportFailure(log, LogLevel.Error, $"Record failed to parse: {read.Error}");
                    continue;
                }

                if (window.Count == MaxConcurrentRecords)
                {
                    await fold.AddAsync(await window.Dequeue(), cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                ProspectCase prospectCase = read.Value;
                window.Enqueue(Task.Run(() => RunRecordAsync(agent, prospectCase, referenceTime, log, inFlightCancellation.Token)));
            }

            await FoldEveryRunAsync(window, fold, cancellationToken);
        }
        finally
        {
            if (window.Count > 0)
            {
                await inFlightCancellation.CancelAsync();

                // Awaited as a plain Task: suppressing the throw is refused on a task with a result.
                await Task.WhenAll((IEnumerable<Task>)window).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        return (recordsRead, parseFailures);
    }

    // Folds every run still in the window, oldest first. O(MaxConcurrentRecords).
    private static async Task FoldEveryRunAsync(Queue<Task<RecordRun>> window, RecordFold fold, CancellationToken cancellationToken)
    {
        while (window.Count > 0)
        {
            await fold.AddAsync(await window.Dequeue(), cancellationToken);
        }
    }

    // The per-call budget for the model composer. Without an override it is the strictest
    // budget any parsed record states, found by a first pass that keeps only the running
    // minimum and logs nothing, since the run's own pass reports every line. The pass reads
    // through its own reader, so the run's reader, which has read nothing yet, still detects a
    // byte order mark once the file is back at its first byte. With an override nothing is read.
    // O(n) time in the input lines and O(1) space.
    private static TimeSpan? ModelCallBudgetFromInput(StreamReader inputReader, TimeSpan? evaluationOverride)
    {
        TimeSpan? budget;
        using (AgentLog.Configure(NullLoggerFactory.Instance))
        using (var firstPass = new StreamReader(inputReader.BaseStream, leaveOpen: true))
        {
            budget = ModelCallBudget.PerCallBudget(
                new JsonlRecordReader().ReadEach(firstPass).Where(read => read.IsSuccess).Select(read => read.Value),
                evaluationOverride);
        }

        inputReader.BaseStream.Position = 0;
        return budget;
    }

    // One record's run, from its log scope to its result, returned rather than written anywhere
    // shared, because up to MaxConcurrentRecords of these run at once. The TaskId scope is opened
    // here and nowhere else, since a second scope in the library rendered every line as
    // "TaskId=x TaskId=x". It opens inside this record's own async flow, which the scope stack
    // LoggerFactory hands both providers follows, so a line one record logs never carries
    // another's TaskId while both run, which
    // RunAsync_RecordsFailWhileOthersAreInFlight_EachFailureCarriesItsOwnTaskId proves.
    private static async Task<RecordRun> RunRecordAsync(
        LeasingMessageAgent agent,
        ProspectCase prospectCase,
        DateTimeOffset referenceTime,
        ILogger<CliRunner> log,
        CancellationToken cancellationToken)
    {
        using IDisposable? scope = log.BeginScope(new Dictionary<string, object> { [LogKeys.TaskId] = prospectCase.TaskId });

        // One line per record naming every defaulted decision input and how many
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Per-record isolation: a bug that throws on one record must not discard the output
            // already produced for every other record. Every input shape has a default, so only a
            // bug reaches here. The log entry is written here, inside the record's scope; the
            // stderr line and the scorecard row are the fold's, in input order. Cancellation is
            // excluded, as in LeasingMessageAgent.RunAsync's own catch, so it is neither logged as
            // an error nor a RecordRun.Failed: it is not a bug, and with up to MaxConcurrentRecords
            // in flight, logging it as one would make a clean shutdown look like several failures.
            // It propagates out of this call and the batch loop's own cancellation ends the batch.
            log.LogError(ex, "Record failed.");
            return new RecordRun.Failed(prospectCase, ex);
        }

        stopwatch.Stop();

        // One measurement, three readers. It times exactly one agent.RunAsync: every compose
        // attempt and model call is inside it, and reading the line and every output write are
        // not. The log line, the diagnostics row and the scored run are handed this one variable,
        // so no two of them can state a different latency for the same record.
        double latencyMs = stopwatch.Elapsed.TotalMilliseconds;

        log.LogInformation("Record processed in {ElapsedMs}ms.", latencyMs);
        return new RecordRun.Completed(prospectCase, ingestNotes, result, latencyMs);
    }

    // Re-scores an existing output file against --input without running the agent. The output
    // carries no task id, so rows pair with parsed records by position and a count mismatch is
    // refused; a safety count and a latency exist only in the run that wrote the file, so both
    // score as not measured.
    private async Task<int> ReplayAsync(
        string inputPath,
        string replayPath,
        string? evalReportPath,
        SemanticJudge? judge,
        ILoggerFactory loggerFactory,
        ILogger<CliRunner> log,
        CancellationToken cancellationToken)
    {
        // Both reader paths get the open guard the output paths have, in the order they are read.
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
        // No batch latency and no batch cost: no record loop ran here, so nothing was timed and
        // nothing was spent, and the scorecard says so rather than printing zeros.
        Scorecard scored = new Evaluator(loggerFactory.CreateLogger<Evaluator>()).Evaluate(aligned.Value);
        Scorecard scorecard = await JudgeAndAppendAsync(scored, aligned.Value, judge, [.. parseFailures.Select(RecordScore.DidNotParse)], cancellationToken);
        if (!await WriteScorecardAsync(scorecard, evalReportPath, log, cancellationToken))
        {
            return CliExitCodes.UsageError;
        }

        return failureCount == 0 ? CliExitCodes.Success : CliExitCodes.PartialFailure;
    }

    // A line that did not parse is one failure row with its line number; there is no task
    // id to scope the log line on, so the line number is the only identity it has. The
    // failure text is returned as well as reported, because the scorecard carries it as a
    // row, so a scorecard read alone never shows a clean run for an input with a bad line.
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
    // Returns false only when --eval-report was passed and could not be written. The
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
            // The same guard mechanism the three batch output streams get, at the write
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

    // One exception-to-Result mechanism for every path this program opens (--log-file, the
    // output streams, and the input, replay and rules files), so the filter and the message
    // wording are stated once rather than restated at each call site. A path the caller can fix
    // (missing directory, no permission, a locked or full volume) is an expected failure, and
    // anything else stays a bug and keeps throwing.
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
    // stream held open across the batch, so a failure at any point of the write is caught here,
    // where a held stream that fails mid-batch throws from inside the writer, past every guard.
    // It shares TryOpen's filter and message wording instead of restating them.
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

    // One guard for every output file the batch writes, so all three fail the same way
    // and each names the flag the caller passed. A null path is the flag not being passed at
    // all, which is a success carrying no stream, not a failure.
    private static Result<StreamWriter?> OpenOutputStream(string flag, string? path) =>
        TryOpenOptional(flag, path, p => new StreamWriter(p));

    // The mirror of OpenOutputStream for the paths a run reads: --input, --replay and --rules.
    // Same filter and same wording deliberately: a path the caller can fix is one class of
    // failure whichever direction the bytes go, and a wider filter here than there would make
    // one program say two things about one operating-system fact. Exit code 1 rather than 2 is
    // the caller's half of that: 2 means some records were processed and some were not, and a
    // file that never opened has no records at all. No null path to answer for, unlike the
    // output flags: both callers reach this only with a path the usage check required.
    private static Result<StreamReader> OpenInputReader(string flag, string path) =>
        TryOpen(flag, path, p => new StreamReader(p));

    // No path is no rules file, and the compiled rules apply. A path that will not open fails
    // the way --input does; a file the loader refuses fails with a line naming the flag and
    // then the loader's failure, which already carries one line per bad row.
    // O(n) time and space in the file's length, the loader's cost.
    private static Result<DecisionRules?> LoadRules(string? path)
    {
        if (path is null)
        {
            return Result<DecisionRules?>.Success(null);
        }

        Result<StreamReader> opened = OpenInputReader("--rules", path);
        if (!opened.IsSuccess)
        {
            return Result<DecisionRules?>.Failure(opened.Error);
        }

        using StreamReader reader = opened.Value;
        Result<DecisionRules> loaded = RulesFileLoader.Load(reader);
        return loaded.IsSuccess
            ? Result<DecisionRules?>.Success(loaded.Value)
            : Result<DecisionRules?>.Failure($"Could not load --rules '{path}':{Environment.NewLine}{loaded.Error}");
    }

    private static string? GetOption(string[] cliArgs, string name)
    {
        int index = Array.IndexOf(cliArgs, name);
        return index >= 0 && index + 1 < cliArgs.Length ? cliArgs[index + 1] : null;
    }

    // The judge model is pinned separately from the composer's, so the two are not the same
    // model, though one vendor key makes them the same family, the setting in which a judge
    // favors its own family's text; grading against the label rather than for quality is the
    // mitigation. Its calls are evaluation, not the product's per-record work, so they are not
    // bounded by the latency budget the records state the way a compose call is.
    private static SemanticJudge BuildJudge(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        string apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException(
                "--judge needs OpenAI:ApiKey. Set it with: dotnet user-secrets set \"OpenAI:ApiKey\" \"<key>\" --project src/Agent.Cli");
        var completionClient = new OpenAiCompletionClient(SharedHttpClient, apiKey, JudgeModel);
        return new SemanticJudge(completionClient, loggerFactory.CreateLogger<SemanticJudge>());
    }

    // The judge's two verdicts on top of a scorecard when the run asked for them, then one row
    // per input the batch could not process. Both paths finish scoring this way. The judge is one
    // signal beside the deterministic checks and never replaces one, and the unprocessed rows go
    // last because the judge pairs scorecard rows with runs by position.
    // O(n) in the scored runs, one judge call each when the judge is on, plus the rebuilt
    // scorecard's O(n) tallies and O(n log n) p95.
    private static async Task<Scorecard> JudgeAndAppendAsync(
        Scorecard scorecard,
        IReadOnlyList<ScoredRun> runs,
        SemanticJudge? judge,
        IReadOnlyList<RecordScore> unprocessedRows,
        CancellationToken cancellationToken)
    {
        Scorecard judged = judge is null ? scorecard : await judge.JudgeAsync(scorecard, runs, cancellationToken);
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
