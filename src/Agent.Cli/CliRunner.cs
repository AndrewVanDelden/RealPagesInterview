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
public sealed class CliRunner(IConfiguration configuration, TextWriter output, TextWriter error, IMessageComposer? composerOverride = null)
{
    private static readonly HttpClient SharedHttpClient = new();

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        string? inputPath = GetOption(args, "--input");
        string? outputPath = GetOption(args, "--output");
        string composerName = GetOption(args, "--composer") ?? "template";
        string? diagnosticsPath = GetOption(args, "--diagnostics");
        string? evalReportPath = GetOption(args, "--eval-report");
        string? logFilePath = GetOption(args, "--log-file");
        string? nowOption = GetOption(args, "--now");
        string? replayPath = GetOption(args, "--replay");

        if (inputPath is null || (outputPath is null && replayPath is null))
        {
            error.WriteLine("Usage: --input <file.jsonl> (--output <file.json> | --replay <file.json>) [--now <ISO-8601 date-time>] [--composer template|openai] [--diagnostics <file.json>] [--eval-report <file.txt>] [--log-file <file.log>]");
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

        if (replayPath is not null)
        {
            return await ReplayAsync(inputPath, replayPath, evalReportPath, loggerFactory, log, cancellationToken);
        }

        var templateFallback = new TemplateMessageComposer();
        IMessageComposer baseComposer;
        try
        {
            baseComposer = composerOverride ?? composerName switch
            {
                "template" => templateFallback,
                "openai" => BuildOpenAiComposer(configuration, loggerFactory),
                _ => throw new ArgumentException($"Unknown composer '{composerName}'. Expected 'template' or 'openai'."),
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

        (List<ProspectCase> cases, int failureCount) = ReadInput(inputPath, log);

        // Output streams are opened here, before the batch loop, deliberately: an invalid
        // output/diagnostics path (bad directory, no write permission) must fail immediately,
        // not after every record has already run through the composer and any LLM calls.
        // outputPath is non-null here: the usage check above requires it when there is no
        // --replay, and the replay path has already returned.
        await using var outputStream = new StreamWriter(outputPath!);
        await using StreamWriter? diagnosticsStream = diagnosticsPath is not null ? new StreamWriter(diagnosticsPath) : null;

        log.LogInformation("Reference time for this run: {ReferenceTime}.", referenceTime.ToString("O", CultureInfo.InvariantCulture));

        var outputs = new List<AgentOutput>();
        var diagnosticsRecords = new List<TaskDiagnostics>();
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

            // D1: one line per record naming every defaulted decision input and every
            // unknown member, before any decision reads them.
            IngestNotes ingestNotes = IngestNotes.Describe(prospectCase);
            log.LogInformation(
                "Ingest: defaulted=[{DefaultedFields}] unknown=[{UnknownMembers}].",
                string.Join(", ", ingestNotes.DefaultedFields),
                string.Join(", ", ingestNotes.UnknownMembers));

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
            scoredRuns.Add(new ScoredRun(prospectCase, result.Output, result.Diagnostics.SafetyViolationCount, stopwatch.Elapsed.TotalMilliseconds));
        }

        log.LogInformation("Batch complete: {Total} record(s), {Failures} failure(s).", cases.Count + failureCount, failureCount);

        var outputWriter = new JsonArrayRecordWriter<AgentOutput>();
        await outputWriter.WriteAllAsync(outputStream, outputs, cancellationToken);

        if (diagnosticsStream is not null)
        {
            var diagnosticsWriter = new JsonArrayRecordWriter<TaskDiagnostics>();
            await diagnosticsWriter.WriteAllAsync(diagnosticsStream, diagnosticsRecords, cancellationToken);
        }

        if (evalReportPath is not null)
        {
            // Scores the results already captured above - never re-runs the agent, so the
            // report describes exactly what was persisted to --output, not a second,
            // possibly different sample (this matters for non-deterministic composers).
            IEvaluator evaluator = new Evaluator(loggerFactory.CreateLogger<Evaluator>());
            await WriteScorecardAsync(evaluator.Evaluate(scoredRuns), evalReportPath, log, cancellationToken);
        }

        return failureCount == 0 ? CliExitCodes.Success : CliExitCodes.PartialFailure;
    }

    // D14: re-score an existing output file against --input without running the agent.
    // Rows pair with records by position; a safety count and a latency exist only in the
    // run that wrote the file, so both score as not measured.
    private async Task<int> ReplayAsync(
        string inputPath,
        string replayPath,
        string? evalReportPath,
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
            log.LogError("Replay refused: {Error}", aligned.Error);
            error.WriteLine($"--replay '{replayPath}': {aligned.Error}");
            return CliExitCodes.UsageError;
        }

        log.LogInformation("Replay: scoring {Count} output(s) from the file; safety and latency are not measured.", aligned.Value.Count);
        IEvaluator evaluator = new Evaluator(loggerFactory.CreateLogger<Evaluator>());
        await WriteScorecardAsync(evaluator.Evaluate(aligned.Value), evalReportPath, log, cancellationToken);

        return failureCount == 0 ? CliExitCodes.Success : CliExitCodes.PartialFailure;
    }

    // A line that did not parse is one failure row with its line number; there is no task
    // id to scope the log line on, so the line number is the only identity it has.
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
            log.LogError("Record failed to parse: {Error}", readResult.Error);
            error.WriteLine($"Record failed to parse: {readResult.Error}");
        }

        return (cases, failureCount);
    }

    // A case missing its labeled expected outcome shows up as an unscoreable row rather
    // than aborting the whole report. The report always goes to the console; the file is
    // optional.
    private async Task WriteScorecardAsync(Scorecard scorecard, string? evalReportPath, ILogger<CliRunner> log, CancellationToken cancellationToken)
    {
        foreach (RecordScore score in scorecard.RecordScores)
        {
            if (score.ScoringError is not null)
            {
                log.LogWarning("Eval: record '{TaskId}' could not be scored: {Error}", score.TaskId, score.ScoringError);
                error.WriteLine($"Eval: record '{score.TaskId}' could not be scored: {score.ScoringError}");
            }
        }

        string report = ScorecardFormatter.Format(scorecard);
        output.Write(report);

        if (evalReportPath is not null)
        {
            await File.WriteAllTextAsync(evalReportPath, report, cancellationToken);
        }
    }

    private static string? GetOption(string[] cliArgs, string name)
    {
        int index = Array.IndexOf(cliArgs, name);
        return index >= 0 && index + 1 < cliArgs.Length ? cliArgs[index + 1] : null;
    }

    private static IMessageComposer BuildOpenAiComposer(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        string apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException(
                "OpenAI:ApiKey is not configured. Set it with: dotnet user-secrets set \"OpenAI:ApiKey\" \"<key>\" --project src/Agent.Cli");
        string model = configuration["OpenAI:Model"] ?? "gpt-4o-mini";

        var completionClient = new OpenAiCompletionClient(SharedHttpClient, apiKey, model);
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
