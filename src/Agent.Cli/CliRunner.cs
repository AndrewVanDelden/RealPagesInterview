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

        // D30: the judge is off unless it is asked for. It is a presence flag, not an
        // option with a value: there is one judge, and its model is pinned rather than
        // chosen per run (playbook step 31).
        bool judgeRequested = args.Contains("--judge", StringComparer.Ordinal);

        if (inputPath is null || (outputPath is null && replayPath is null))
        {
            error.WriteLine("Usage: --input <file.jsonl> (--output <file.json> | --replay <file.json>) [--now <ISO-8601 date-time>] [--composer template|openai] [--judge] [--diagnostics <file.json>] [--review-queue <file.json>] [--eval-report <file.txt>] [--log-file <file.log>]");
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

        // FileLoggerProvider is disposed here, explicitly, rather than trusted to
        // LoggerFactory's own Dispose: an ILoggerProvider instance handed to AddProvider
        // is not one the DI container underneath LoggerFactory.Create constructed itself,
        // and is therefore not reliably disposed alongside it - a real leak this project
        // hit first as a locked log file in its own tests, not as a hypothetical.
        FileLoggerProvider? fileLoggerProvider;
        try
        {
            fileLoggerProvider = logFilePath is not null ? new FileLoggerProvider(logFilePath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Could not open --log-file '{logFilePath}': {ex.ToDiagnosticString()}");
            return CliExitCodes.UsageError;
        }

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

        (List<ProspectCase> cases, int failureCount) = ReadInput(inputPath, log);

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
                ComposerNames.OpenAi => BuildOpenAiComposer(configuration, loggerFactory, ModelCallBudget.PerCallBudget(cases)),
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

        var safetyValidator = new SafetyValidator();
        IMessageComposer composer = new ValidatingMessageComposer(
            baseComposer,
            safetyValidator,
            templateFallback,
            loggerFactory.CreateLogger<ValidatingMessageComposer>());

        var agent = new LeasingMessageAgent(
            new ConsentGate(),
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
        // outputPath is non-null here: the usage check above requires it when there is no
        // --replay, and the replay path has already returned.
        await using var outputStream = new StreamWriter(outputPath!);
        await using StreamWriter? diagnosticsStream = diagnosticsPath is not null ? new StreamWriter(diagnosticsPath) : null;
        await using StreamWriter? reviewQueueStream = reviewQueuePath is not null ? new StreamWriter(reviewQueuePath) : null;

        log.LogInformation("Reference time for this run: {ReferenceTime}.", referenceTime.ToString("O", CultureInfo.InvariantCulture));

        var outputs = new List<AgentOutput>();
        var diagnosticsRecords = new List<TaskDiagnostics>();
        var reviewQueue = new List<ReviewQueueEntry>();
        var scoredRuns = new List<ScoredRun>();

        foreach (ProspectCase prospectCase in cases)
        {
            // Nothing in the default (template) composer path observes cancellationToken
            // itself, so without this check a cancelled run kept grinding through every
            // remaining record instead of stopping - the only sign anything was wrong was
            // an exception from the output-writing step at the very end, after all the
            // work was already done.
            cancellationToken.ThrowIfCancellationRequested();

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
                // shape has a default (D1), so only a bug reaches here.
                failureCount++;
                log.LogError(ex, "Record failed.");
                error.WriteLine($"Record '{prospectCase.TaskId}' failed: {ex.ToDiagnosticString()}");
                continue;
            }

            stopwatch.Stop();
            log.LogInformation("Record processed in {ElapsedMs}ms.", stopwatch.Elapsed.TotalMilliseconds);
            outputs.Add(result.Output);
            diagnosticsRecords.Add(new TaskDiagnostics(prospectCase.TaskId, result.Diagnostics, ingestNotes));

            // D43: one row per record the safety gate suppressed, and nothing else. A record
            // with no consented channel is not here (not contactable is the correct decision,
            // not a failure) and neither is a composition that produced no draft (there is
            // nothing to review), which is exactly what a null RejectedDraft says.
            if (result.RejectedDraft is { } rejectedDraft)
            {
                reviewQueue.Add(new ReviewQueueEntry(prospectCase.TaskId, rejectedDraft.Violations, rejectedDraft.Message));
            }

            scoredRuns.Add(new ScoredRun(prospectCase, result.Output, result.Diagnostics.SafetyViolationCount, stopwatch.Elapsed.TotalMilliseconds));
        }

        log.LogInformation("Batch complete: {Total} record(s), {Failures} failure(s).", recordsRead, failureCount);

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
            Scorecard scorecard = await ScoreAsync(scoredRuns, judge, loggerFactory, cancellationToken);
            await WriteScorecardAsync(scorecard, evalReportPath, log, cancellationToken);
        }

        // A queued record leaves at exit 0 (D43): suppression is a correct pipeline outcome
        // and failureCount counts records the pipeline could not process at all.
        return failureCount == 0 ? CliExitCodes.Success : CliExitCodes.PartialFailure;
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
        (List<ProspectCase> cases, int failureCount) = ReadInput(inputPath, log);

        Result<IReadOnlyList<AgentOutput>> outputs;
        using (var replayReader = new StreamReader(replayPath))
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
        Scorecard scorecard = await ScoreAsync(aligned.Value, judge, loggerFactory, cancellationToken);
        await WriteScorecardAsync(scorecard, evalReportPath, log, cancellationToken);

        return failureCount == 0 ? CliExitCodes.Success : CliExitCodes.PartialFailure;
    }

    // A line that did not parse is one failure row with its line number; there is no task
    // id to scope the log line on, so the line number is the only identity it has.
    // O(n) in the file size: one line parsed, and on failure reported, per iteration.
    private (List<ProspectCase> Cases, int FailureCount) ReadInput(string inputPath, ILogger<CliRunner> log)
    {
        IReadOnlyList<Result<ProspectCase>> readResults;
        using (var inputReader = new StreamReader(inputPath))
        {
            readResults = new JsonlRecordReader().ReadAll(inputReader);
        }

        var cases = new List<ProspectCase>(readResults.Count);
        int failureCount = 0;

        foreach (Result<ProspectCase> readResult in readResults)
        {
            if (readResult.IsSuccess)
            {
                cases.Add(readResult.Value);
                continue;
            }

            failureCount++;
            ReportFailure(log, LogLevel.Error, $"Record failed to parse: {readResult.Error}");
        }

        return (cases, failureCount);
    }

    // A case missing its labeled expected outcome shows up as an unscoreable row rather
    // than aborting the whole report. The report always goes to the console; the file is
    // optional. O(n) in the batch size: one record's scoring error reported per iteration,
    // plus one file write.
    private async Task WriteScorecardAsync(Scorecard scorecard, string? evalReportPath, ILogger<CliRunner> log, CancellationToken cancellationToken)
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
            await File.WriteAllTextAsync(evalReportPath, report, cancellationToken);
        }
    }

    // The log line and the stderr line always carry the same text: one failure, reported
    // through both channels, never independently worded.
    private void ReportFailure(ILogger log, LogLevel level, string message)
    {
        log.Log(level, message);
        error.WriteLine(message);
    }

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
    private static async Task<Scorecard> ScoreAsync(
        IReadOnlyList<ScoredRun> runs,
        SemanticJudge? judge,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        IEvaluator evaluator = new Evaluator(loggerFactory.CreateLogger<Evaluator>());
        Scorecard scorecard = evaluator.Evaluate(runs);

        return judge is null ? scorecard : await judge.JudgeAsync(scorecard, runs, cancellationToken);
    }

    private static IMessageComposer BuildOpenAiComposer(IConfiguration configuration, ILoggerFactory loggerFactory, TimeSpan? callTimeout)
    {
        string apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException(
                "OpenAI:ApiKey is not configured. Set it with: dotnet user-secrets set \"OpenAI:ApiKey\" \"<key>\" --project src/Agent.Cli");
        string model = configuration["OpenAI:Model"] ?? "gpt-4o-mini";

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
