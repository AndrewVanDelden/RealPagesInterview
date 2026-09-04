using System.Diagnostics;
using Agent.Composition;
using Agent.Decisions;
using Agent.Domain;
using Agent.Evaluation;
using Agent.Ingest;
using Agent.Orchestration;
using Agent.Safety;
using Microsoft.Extensions.Configuration;

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

        if (inputPath is null || outputPath is null)
        {
            error.WriteLine("Usage: --input <file.jsonl> --output <file.json> [--composer template|openai] [--diagnostics <file.json>] [--eval-report <file.txt>]");
            return CliExitCodes.UsageError;
        }

        var templateFallback = new TemplateMessageComposer();
        IMessageComposer baseComposer;
        try
        {
            baseComposer = composerName switch
            {
                "template" => templateFallback,
                "openai" => BuildOpenAiComposer(configuration),
                _ => throw new ArgumentException($"Unknown composer '{composerName}'. Expected 'template' or 'openai'."),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            error.WriteLine(ex.Message);
            return CliExitCodes.UsageError;
        }

        var safetyValidator = new SafetyValidator();
        IMessageComposer composer = new ValidatingMessageComposer(baseComposer, safetyValidator, templateFallback);

        var agent = new LeasingMessageAgent(
            new ConsentGate(),
            new ChannelSelector(),
            composer,
            safetyValidator,
            new SendScheduler(),
            new NextActionPlanner());

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
                error.WriteLine($"Record '{prospectCase.TaskId}' failed: {ex.Message}");
                continue;
            }

            stopwatch.Stop();
            outputs.Add(result.Output);
            diagnosticsRecords.Add(new TaskDiagnostics(prospectCase.TaskId, result.Diagnostics));
            scoredRuns.Add(new ScoredRun(prospectCase, result, stopwatch.Elapsed.TotalMilliseconds));
        }

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
            IEvaluator evaluator = new Evaluator();
            Scorecard scorecard = evaluator.Evaluate(scoredRuns);

            foreach (RecordScore score in scorecard.RecordScores)
            {
                if (score.ScoringError is not null)
                {
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

    private static IMessageComposer BuildOpenAiComposer(IConfiguration configuration)
    {
        string apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException(
                "OpenAI:ApiKey is not configured. Set it with: dotnet user-secrets set \"OpenAI:ApiKey\" \"<key>\" --project src/Agent.Cli");
        string model = configuration["OpenAI:Model"] ?? "gpt-4o-mini";

        var completionClient = new OpenAiCompletionClient(SharedHttpClient, apiKey, model);
        return new OpenAiMessageComposer(completionClient);
    }
}
