using System.Text.Json;
using Agent.Cli;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Agent.Cli.Tests;

public class CliRunnerTests
{
    private static string RecordJson(
        string taskId,
        string moveDateTarget,
        string lastInteraction,
        string timeZoneId = "America/Chicago",
        bool includeExpected = false)
    {
        string expectedSuffix = includeExpected
            ? ",\"expected\":{\"next_message\":{\"channel\":\"sms\",\"body\":\"hi\"},\"next_action\":{\"type\":\"start_cadence\"}}"
            : string.Empty;

        return "{\"task_id\":\"" + taskId + "\",\"persona\":\"prospect\",\"lifecycle_stage\":\"new\"," +
            "\"consent\":{\"email_opt_in\":true,\"sms_opt_in\":true,\"voice_opt_in\":false}," +
            "\"channel_preferences\":[\"sms\"]," +
            "\"input\":{\"property_name\":\"Oak Ridge\",\"move_date_target\":\"" + moveDateTarget + "\",\"last_interaction\":\"" + lastInteraction + "\",\"timezone\":\"" + timeZoneId + "\",\"language\":\"en\",\"profile\":{\"first_name\":\"Taylor\"}}," +
            "\"assertions\":{\"required_states\":[],\"constraints\":{\"no_pii_leak\":true,\"include_opt_out_instructions\":true,\"primary_cta\":\"book_tour\"}}," +
            "\"thresholds\":{\"p95_latency_ms\":2000,\"personalization_score_min\":0.85,\"reply_classification_f1_min\":0.9,\"safety_violations_max\":0}" +
            expectedSuffix + "}";
    }

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    private static string TempFilePath(string extension = ".jsonl")
    {
        return Path.Combine(Path.GetTempPath(), $"cli-runner-tests-{Guid.NewGuid():N}{extension}");
    }

    [Fact]
    public async Task RunAsync_MissingInputAndOutput_WritesUsageAndReturnsUsageError()
    {
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        int exitCode = await runner.RunAsync([]);

        Assert.Equal(CliExitCodes.UsageError, exitCode);
        Assert.Contains("Usage:", errorWriter.ToString());
    }

    [Fact]
    public async Task RunAsync_UnknownComposer_WritesCleanErrorAndReturnsUsageError()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--composer", "mock"]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains("Unknown composer 'mock'", errorWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task RunAsync_OpenAiComposerWithoutApiKey_WritesCleanErrorAndReturnsUsageError()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--composer", "openai"]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains("OpenAI:ApiKey is not configured", errorWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task RunAsync_TemplateComposerTwoValidRecords_WritesTwoOutputRecordsAndReturnsSuccess()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string content = string.Join(
            Environment.NewLine,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"),
            RecordJson("t2", "2026-03-01", "2025-12-08T15:04:00Z"));
        await File.WriteAllTextAsync(inputPath, content);
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath]);

            Assert.Equal(CliExitCodes.Success, exitCode);
            using JsonDocument output = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            Assert.Equal(JsonValueKind.Array, output.RootElement.ValueKind);
            Assert.Equal(2, output.RootElement.GetArrayLength());
            Assert.True(output.RootElement[0].TryGetProperty("next_message", out _));
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task RunAsync_OneRecordFailsPlanning_OtherRecordStillWrittenAndReturnsPartialFailure()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        // t1 is valid (move date after last interaction). t2's move date precedes its
        // last interaction, so NextActionPlanner.Plan throws ArgumentOutOfRangeException.
        string content = string.Join(
            Environment.NewLine,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"),
            RecordJson("t2", "2025-01-01", "2025-12-08T15:04:00Z"));
        await File.WriteAllTextAsync(inputPath, content);
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath]);

            Assert.Equal(CliExitCodes.PartialFailure, exitCode);
            using JsonDocument output = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            Assert.Equal(1, output.RootElement.GetArrayLength());
            Assert.Contains("t2", errorWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task RunAsync_DiagnosticsPathProvided_WritesOneTypedDiagnosticsRecordPerRecord()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string diagnosticsPath = TempFilePath();
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--diagnostics", diagnosticsPath]);

            using JsonDocument diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(diagnosticsPath));
            Assert.Equal(1, diagnostics.RootElement.GetArrayLength());
            JsonElement firstRecord = diagnostics.RootElement[0];
            Assert.Equal("t1", firstRecord.GetProperty("task_id").GetString());
            Assert.True(firstRecord.TryGetProperty("diagnostics", out _));
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(diagnosticsPath);
        }
    }

    [Fact]
    public async Task RunAsync_OpenAiComposerWithApiKeyAndNoRecords_BuildsComposerUsingDefaultModel()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        await File.WriteAllTextAsync(inputPath, string.Empty);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new("OpenAI:ApiKey", "fake-key-for-coverage")])
            .Build();
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(configuration, outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--composer", "openai"]);

            Assert.Equal(CliExitCodes.Success, exitCode);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task RunAsync_OpenAiComposerWithApiKeyAndModelAndNoRecords_BuildsComposerUsingConfiguredModel()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        await File.WriteAllTextAsync(inputPath, string.Empty);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new("OpenAI:ApiKey", "fake-key-for-coverage"), new("OpenAI:Model", "gpt-4o")])
            .Build();
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(configuration, outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--composer", "openai"]);

            Assert.Equal(CliExitCodes.Success, exitCode);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task RunAsync_NoDiagnosticsPath_DoesNotThrow()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath]);

            Assert.Equal(CliExitCodes.Success, exitCode);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task RunAsync_EvalReportPathProvided_WritesScorecardToConsoleAndFile()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string evalReportPath = TempFilePath(".txt");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true));
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--eval-report", evalReportPath]);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("t1", outputWriter.ToString());
            Assert.Contains("Overall:", outputWriter.ToString());
            string fileContent = await File.ReadAllTextAsync(evalReportPath);
            Assert.Contains("t1", fileContent);
            Assert.Contains("Overall:", fileContent);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(evalReportPath);
        }
    }

    // A record missing its labeled expected outcome no longer aborts the whole eval report -
    // it shows up as an unscoreable row (logged to stderr for visibility), and the CLI's
    // exit code still reflects the main --output pass, not the optional eval rehearsal.
    [Fact]
    public async Task RunAsync_EvalReportRequestedButRecordHasNoExpectedOutcome_ReportsUnscoreableRowAndStillSucceeds()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string evalReportPath = TempFilePath(".txt");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: false));
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--eval-report", evalReportPath]);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("could not be scored", errorWriter.ToString());
            string fileContent = await File.ReadAllTextAsync(evalReportPath);
            Assert.Contains("ERROR", fileContent);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(evalReportPath);
        }
    }

    // The main --output pass never requires Expected to be populated, so a perfectly normal
    // mixed-label input file must not crash eval-reporting: the labeled record scores
    // normally and the unlabeled one shows up as an unscoreable row, side by side.
    [Fact]
    public async Task RunAsync_EvalReportWithMixedLabeledAndUnlabeledRecords_ScoresLabeledAndFlagsUnlabeled()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string evalReportPath = TempFilePath(".txt");
        string content = string.Join(
            Environment.NewLine,
            RecordJson("labeled", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true),
            RecordJson("unlabeled", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: false));
        await File.WriteAllTextAsync(inputPath, content);
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--eval-report", evalReportPath]);

            Assert.Equal(CliExitCodes.Success, exitCode);
            string fileContent = await File.ReadAllTextAsync(evalReportPath);
            Assert.Contains("labeled", fileContent);
            Assert.Contains("unlabeled", fileContent);
            Assert.Contains("ERROR", fileContent);
            Assert.Contains("PASS", fileContent);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(evalReportPath);
        }
    }
}
