using System.Diagnostics;
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
public sealed class CliRunner(IConfiguration configuration, TextWriter output, TextWriter error)
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

        if (inputPath is null || outputPath is null)
        {
            error.WriteLine("Usage: --input <file.jsonl> --output <file.json> [--composer template|openai] [--diagnostics <file.json>] [--eval-report <file.txt>] [--log-file <file.log>]");
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

        var templateFallback = new TemplateMessageComposer();
        IMessageComposer baseComposer;
        try
        {
            baseComposer = composerName switch
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

        IReadOnlyList<ProspectCase> cases;
        using (var inputReader = new StreamReader(inputPath))
        {
            cases = new JsonlRecordReader().ReadAll(inputReader);
        }

        // Output streams are opened here, before the batch loop, deliberately: an invalid
        // output/diagnostics path (bad directory, no write permission) must fail immediately,
        // not after every record has already run through the composer and any LLM calls.
        await using var outputStream = new StreamWriter(outputPath);
        await using StreamWriter? diagnosticsStream = diagnosticsPath is not null ? new StreamWriter(diagnosticsPath) : null;

        var outputs = new List<AgentOutput>();
        var diagnosticsRecords = new List<TaskDiagnostics>();
        var scoredRuns = new List<ScoredRun>();
        int failureCount = 0;

        foreach (ProspectCase prospectCase in cases)
        {
            // Nothing in the default (template) composer path observes cancellationToken
            // itself, so without this check a cancelled run kept grinding through every
            // remaining record instead of stopping - the only sign anything was wrong was
            // an exception from the output-writing step at the very end, after all the
            // work was already done.
            cancellationToken.ThrowIfCancellationRequested();

            using IDisposable? scope = log.BeginScope(new Dictionary<string, object> { [LogKeys.TaskId] = prospectCase.TaskId });

            Stopwatch stopwatch = Stopwatch.StartNew();
            AgentRunResult result;
            try
            {
                result = await agent.RunAsync(prospectCase, cancellationToken);
            }
            catch (Exception ex)
            {
                // Per-record isolation: one malformed record (bad move date, unrecognized
                // timezone, an unsalvageable compose-validate failure) must not discard the
                // output already produced for every other record in the batch.
                failureCount++;
                log.LogError(ex, "Record failed.");
                error.WriteLine($"Record '{prospectCase.TaskId}' failed: {ex.ToDiagnosticString()}");
                continue;
            }

            stopwatch.Stop();
            log.LogInformation("Record processed in {ElapsedMs}ms.", stopwatch.Elapsed.TotalMilliseconds);
            outputs.Add(result.Output);
            diagnosticsRecords.Add(new TaskDiagnostics(prospectCase.TaskId, result.Diagnostics));
            scoredRuns.Add(new ScoredRun(prospectCase, result, stopwatch.Elapsed.TotalMilliseconds));
        }

        log.LogInformation("Batch complete: {Total} record(s), {Failures} failure(s).", cases.Count, failureCount);

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
            // A case missing its labeled expected outcome shows up as an unscoreable row
            // rather than aborting the whole report.
            IEvaluator evaluator = new Evaluator(loggerFactory.CreateLogger<Evaluator>());
            Scorecard scorecard = evaluator.Evaluate(scoredRuns);

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
            await File.WriteAllTextAsync(evalReportPath, report, cancellationToken);
        }

        return failureCount == 0 ? CliExitCodes.Success : CliExitCodes.PartialFailure;
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
    // FileLoggerProvider - the "a log file" gap TalkingPoints.md's Sprint 8 audit flagged
    // as missing from this codebase entirely.
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
