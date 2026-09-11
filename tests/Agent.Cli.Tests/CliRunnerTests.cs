using System.Globalization;
using System.Text.Json;
using Agent.Cli;
using Agent.Cli.Tests.TestSupport;
using Agent.Composition;
using Agent.Evaluation;
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

    // A record the template composer cannot compose safely for, using nothing but the
    // record's own data: city_interest is written into the body verbatim ("We heard you're
    // looking in families only."), and "families only" is a steering term the fair-housing
    // check of D38 fails on. The record asserts fair_housing_check_passed so the states map
    // has to answer for it.
    private static string SteeringRecordJson(string taskId) =>
        RecordJson(taskId, "2026-01-10", "2025-12-08T15:04:00Z")
            .Replace("\"first_name\":\"Taylor\"", "\"first_name\":\"Taylor\",\"city_interest\":\"families only\"", StringComparison.Ordinal)
            .Replace("\"required_states\":[]", "\"required_states\":[\"fair_housing_check_passed\"]", StringComparison.Ordinal);

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

    // D70: the budget an evaluation run states for the composer's model calls is a positive whole
    // number of milliseconds; anything else bounds nothing a call could satisfy.
    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1.5")]
    public async Task RunAsync_ModelCallBudgetNotAPositiveWholeNumber_WritesCleanErrorAndReturnsUsageError(string value)
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--composer", "openai", "--model-call-budget-ms", value]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains($"--model-call-budget-ms '{value}' is not a positive whole number of milliseconds.", errorWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // D70: on any composer but openai the flag would bound nothing, and a flag that silently
    // does nothing is a flag someone trusts.
    [Fact]
    public async Task RunAsync_ModelCallBudgetWithoutTheOpenAiComposer_WritesCleanErrorAndReturnsUsageError()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--model-call-budget-ms", "30000"]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains("--model-call-budget-ms applies only to --composer openai.", errorWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // D70: the flag changes what bounds a model call, never whether the key a call needs is
    // required.
    [Fact]
    public async Task RunAsync_ModelCallBudgetWithTheOpenAiComposerAndNoKey_StillRefusesOnTheMissingKey()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--composer", "openai", "--model-call-budget-ms", "30000"]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains("OpenAI:ApiKey is not configured", errorWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // D71: a line that did not parse is a failure the scorecard shows, not only the exit code:
    // one ERROR row naming the line, counted in the overall and never passed.
    [Fact]
    public async Task RunAsync_OneLineFailsToParse_ScorecardCarriesAnErrorRowCountedInTheOverall()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string evalReportPath = TempFilePath(".txt");
        string content = string.Join(
            Environment.NewLine,
            "{not valid json",
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true));
        await File.WriteAllTextAsync(inputPath, content);
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter());

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--eval-report", evalReportPath]);

            Assert.Equal(CliExitCodes.PartialFailure, exitCode);
            string report = await File.ReadAllTextAsync(evalReportPath);
            Assert.Contains("(did not parse)", report);
            Assert.Contains("ERROR: Line 1 failed to parse", report);
            Assert.Contains("/2 passed", report);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(evalReportPath);
        }
    }

    // D71: a record that threw inside the agent left the batch loop by a continue and vanished
    // from the scorecard the same way. It is a row under its own task id, its reason the
    // exception type alone (D46), since the message of an exception is not known to be safe.
    [Fact]
    public async Task RunAsync_OneRecordThrows_ScorecardCarriesAnErrorRowUnderItsTaskId()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string evalReportPath = TempFilePath(".txt");
        string content = string.Join(
            Environment.NewLine,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true),
            RecordJson("t2", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true));
        await File.WriteAllTextAsync(inputPath, content);
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter(), new ThrowingComposer("t2"));

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--eval-report", evalReportPath]);

            Assert.Equal(CliExitCodes.PartialFailure, exitCode);
            string report = await File.ReadAllTextAsync(evalReportPath);
            Assert.Contains(
                report.Split('\n'),
                line => line.StartsWith("t2 ", StringComparison.Ordinal) && line.Contains("ERROR: Record failed: InvalidOperationException", StringComparison.Ordinal));
            Assert.DoesNotContain("Injected fault", report);
            Assert.Contains("/2 passed", report);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(evalReportPath);
        }
    }

    // D71: replay reads --input through the same reader, so a line that did not parse is the
    // same row there.
    [Fact]
    public async Task RunAsync_ReplayWithAnUnparsableInputLine_ScorecardCarriesAnErrorRow()
    {
        string inputPath = TempFilePath();
        string replayPath = TempFilePath(".json");
        string evalReportPath = TempFilePath(".txt");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true) + Environment.NewLine + "{bad");
        await File.WriteAllTextAsync(replayPath, "[{\"next_message\":{\"channel\":\"none\"},\"next_action\":{\"type\":\"no_op\"}}]");
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter());

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--replay", replayPath, "--eval-report", evalReportPath]);

            Assert.Equal(CliExitCodes.PartialFailure, exitCode);
            string report = await File.ReadAllTextAsync(evalReportPath);
            Assert.Contains("(did not parse)", report);
            Assert.Contains("ERROR: Line 2 failed to parse", report);
            Assert.Contains("/2 passed", report);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(replayPath);
            File.Delete(evalReportPath);
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

    // Without this check, a cancelled batch kept grinding through every remaining record -
    // nothing in the default (template) composer path observes cancellationToken itself, so
    // the only way the CLI can honor a cancellation request between records is to check it
    // proactively, rather than relying on agent.RunAsync to throw for it.
    [Fact]
    public async Task RunAsync_TokenAlreadyCancelled_ThrowsRatherThanProcessingAnyRecords()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string content = string.Join(
            Environment.NewLine,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"),
            RecordJson("t2", "2026-01-10", "2025-12-08T15:04:00Z"));
        await File.WriteAllTextAsync(inputPath, content);
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(["--input", inputPath, "--output", outputPath], cts.Token));

            Assert.DoesNotContain("failed", errorWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // Per-record isolation. No input can make a record throw any more (D1 gives every
    // shape a default), so the fault is injected through the composer seam.
    [Fact]
    public async Task RunAsync_OneRecordThrows_OtherRecordStillWrittenAndReturnsPartialFailure()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string content = string.Join(
            Environment.NewLine,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"),
            RecordJson("t2", "2026-01-10", "2025-12-08T15:04:00Z"));
        await File.WriteAllTextAsync(inputPath, content);
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter, new ThrowingComposer("t2"));

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

    // D37 and D14: records run at the same time and finish in whatever order they finish, and
    // every file the batch writes still lists them in input order, because --replay pairs output
    // rows with input records by position. StaggeredComposer holds the three records until all
    // are composing at once, which only a concurrent loop reaches, and releases the last input
    // record first. t1 and t2 are refused at the safety gate and t3 is not, so the output, the
    // diagnostics, the review queue and the scorecard each carry an order that can be read.
    [Fact]
    public async Task RunAsync_RecordsFinishInReverseInputOrder_EveryFileKeepsInputOrder()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string diagnosticsPath = TempFilePath(".json");
        string reviewQueuePath = TempFilePath(".json");
        string evalReportPath = TempFilePath(".txt");
        string content = string.Join(
            Environment.NewLine,
            SteeringRecordJson("t1"),
            SteeringRecordJson("t2"),
            RecordJson("t3", "2026-01-10", "2025-12-08T15:04:00Z"));
        await File.WriteAllTextAsync(inputPath, content);
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter(), new StaggeredComposer(["t1", "t2", "t3"]));

        try
        {
            int exitCode = await runner.RunAsync(
                ["--input", inputPath, "--output", outputPath, "--diagnostics", diagnosticsPath, "--review-queue", reviewQueuePath, "--eval-report", evalReportPath]);

            Assert.Equal(CliExitCodes.Success, exitCode);

            using JsonDocument output = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            Assert.Equal(
                new[] { "none", "none", "sms" },
                output.RootElement.EnumerateArray().Select(row => row.GetProperty("next_message").GetProperty("channel").GetString()).ToArray());

            using JsonDocument diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(diagnosticsPath));
            Assert.Equal(
                new[] { "t1", "t2", "t3" },
                diagnostics.RootElement.EnumerateArray().Select(row => row.GetProperty("task_id").GetString()).ToArray());

            using JsonDocument queue = JsonDocument.Parse(await File.ReadAllTextAsync(reviewQueuePath));
            Assert.Equal(
                new[] { "t1", "t2" },
                queue.RootElement.EnumerateArray().Select(row => row.GetProperty("task_id").GetString()).ToArray());

            string[] scorecardTaskIds = (await File.ReadAllLinesAsync(evalReportPath))
                .Select(line => line.Split('|')[0].Trim())
                .Where(cell => cell is "t1" or "t2" or "t3")
                .ToArray();
            Assert.Equal(new[] { "t1", "t2", "t3" }, scorecardTaskIds);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(diagnosticsPath);
            File.Delete(reviewQueuePath);
            File.Delete(evalReportPath);
        }
    }

    // D37 and D16: the TaskId scope is per record while records overlap. StaggeredComposer holds
    // three records composing at once and then fails each with a fault naming it, so every
    // failure is logged while another record's scope is still open. Each "Record failed." entry
    // ends in its own TaskId and no other, and the stderr lines, written once the batch is done,
    // follow input order rather than the reverse order the records failed in.
    [Fact]
    public async Task RunAsync_RecordsFailWhileOthersAreInFlight_EachFailureCarriesItsOwnTaskId()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string content = string.Join(
            Environment.NewLine,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"),
            RecordJson("t2", "2026-01-10", "2025-12-08T15:04:00Z"),
            RecordJson("t3", "2026-01-10", "2025-12-08T15:04:00Z"));
        await File.WriteAllTextAsync(inputPath, content);
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), errorWriter, new StaggeredComposer(["t1", "t2", "t3"], throwInjectedFault: true));

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath]);

            Assert.Equal(CliExitCodes.PartialFailure, exitCode);
            string errorText = errorWriter.ToString();
            string[] lines = errorText.Split(Environment.NewLine);
            foreach (string taskId in new[] { "t1", "t2", "t3" })
            {
                int entry = Array.FindIndex(lines, line => line.EndsWith($": Record failed. TaskId={taskId}", StringComparison.Ordinal));
                Assert.True(entry >= 0, $"No 'Record failed.' entry scoped to {taskId} alone.");
                Assert.Contains($"Injected fault for '{taskId}'", lines[entry + 1], StringComparison.Ordinal);
            }

            int t1Line = errorText.IndexOf("Record 't1' failed", StringComparison.Ordinal);
            int t2Line = errorText.IndexOf("Record 't2' failed", StringComparison.Ordinal);
            int t3Line = errorText.IndexOf("Record 't3' failed", StringComparison.Ordinal);
            Assert.True(t1Line >= 0 && t1Line < t2Line && t2Line < t3Line, "stderr's record failures are not in input order.");
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // Memory stays bounded because a finished record is folded, its rows written and its stderr
    // line printed, as soon as every earlier record has finished, and at most four records run
    // ahead of the fold. t1 fails at once, so the fifth record, t5, can start only after t1 has
    // left the window, and by then t1's stderr line is already out. A loop that held every
    // record until the batch ended would print that line only after t5 had composed.
    [Fact]
    public async Task RunAsync_RecordPastTheWindow_StartsOnlyAfterTheFirstRecordIsFolded()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string[] records = Enumerable.Range(1, 6).Select(number => RecordJson($"t{number}", "2026-01-10", "2025-12-08T15:04:00Z")).ToArray();
        await File.WriteAllTextAsync(inputPath, string.Join(Environment.NewLine, records));
        var errorWriter = new LineSignalingWriter("Record 't1' failed");
        var composer = new FoldObservingComposer("t1", "t5", errorWriter.Seen);
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), errorWriter, composer);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath]);

            Assert.Equal(CliExitCodes.PartialFailure, exitCode);
            Assert.True(composer.EarlierRecordFoldedWhenObserverComposed);
            using JsonDocument output = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            Assert.Equal(5, output.RootElement.GetArrayLength());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // The batch reads the input as it runs rather than all of it first, so memory does not grow
    // with the file. t1 composes before line 3, the malformed one, has been reported, which a
    // loop that read and reported every line before starting a record could not do.
    [Fact]
    public async Task RunAsync_MalformedLineAfterRecords_FirstRecordComposesBeforeTheLineIsReported()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string content = string.Join(
            Environment.NewLine,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"),
            RecordJson("t2", "2026-01-10", "2025-12-08T15:04:00Z"),
            "{not valid json",
            RecordJson("t3", "2026-01-10", "2025-12-08T15:04:00Z"));
        await File.WriteAllTextAsync(inputPath, content);
        var errorWriter = new LineSignalingWriter("Record failed to parse: Line 3");
        var composer = new FoldObservingComposer("no-such-record", "t1", errorWriter.Seen);
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), errorWriter, composer);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath]);

            Assert.Equal(CliExitCodes.PartialFailure, exitCode);
            Assert.False(composer.EarlierRecordFoldedWhenObserverComposed);
            using JsonDocument output = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            Assert.Equal(3, output.RootElement.GetArrayLength());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // stderr's failure lines follow input order whichever kind they are: a record that threw on
    // line 1 is reported before the line 2 that did not parse.
    [Fact]
    public async Task RunAsync_RecordThrowsBeforeAMalformedLine_StderrKeepsInputOrder()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string content = string.Join(
            Environment.NewLine,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"),
            "{not valid json",
            RecordJson("t2", "2026-01-10", "2025-12-08T15:04:00Z"));
        await File.WriteAllTextAsync(inputPath, content);
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), errorWriter, new ThrowingComposer("t1"));

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath]);

            Assert.Equal(CliExitCodes.PartialFailure, exitCode);
            string[] failureLines = errorWriter.ToString()
                .Split(Environment.NewLine)
                .Where(line => line.StartsWith("Record ", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(2, failureLines.Length);
            Assert.StartsWith("Record 't1' failed: ", failureLines[0], StringComparison.Ordinal);
            Assert.StartsWith("Record failed to parse: Line 2 failed to parse", failureLines[1], StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // The records read are the non-blank lines, parsed or not, whether the batch holds them all
    // or reads them one at a time.
    [Fact]
    public async Task RunAsync_BlankAndMalformedLines_BatchCompleteCountsEveryNonBlankLine()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string logFilePath = TempFilePath(".log");
        string content = string.Join(
            Environment.NewLine,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"),
            string.Empty,
            "{not valid json",
            "   ",
            RecordJson("t2", "2026-01-10", "2025-12-08T15:04:00Z"));
        await File.WriteAllTextAsync(inputPath, content);
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter());

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--log-file", logFilePath]);

            Assert.Equal(CliExitCodes.PartialFailure, exitCode);
            Assert.Contains("Batch complete: 3 record(s), 1 failure(s),", await File.ReadAllTextAsync(logFilePath));
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            TestFiles.DeleteWithRetry(logFilePath);
        }
    }

    // With the model composer and no evaluation budget, the strictest stated budget is read in a
    // first pass over the file before the run. That pass is silent and the run starts again from
    // the first byte, byte order mark included, so every record runs once and every warning and
    // failure line appears once. No record has a consented channel, so no model call is made.
    [Fact]
    public async Task RunAsync_OpenAiComposerReadsTheBudgetFirst_EveryRecordRunsAndIsReportedOnce()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string NotContactable(string taskId) =>
            RecordJson(taskId, "2026-01-10", "2025-12-08T15:04:00Z")
                .Replace("\"email_opt_in\":true", "\"email_opt_in\":false", StringComparison.Ordinal)
                .Replace("\"sms_opt_in\":true", "\"sms_opt_in\":false", StringComparison.Ordinal);
        string content = string.Join(
            Environment.NewLine,
            NotContactable("t1"),
            "{not valid json",
            NotContactable("t2")[..^1] + ",\"expected\":\"not an object\"}");
        await File.WriteAllTextAsync(inputPath, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new("OpenAI:ApiKey", "fake-key-for-coverage")])
            .Build();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(configuration, new StringWriter(), errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--composer", "openai"]);

            Assert.Equal(CliExitCodes.PartialFailure, exitCode);
            using JsonDocument output = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            Assert.Equal(2, output.RootElement.GetArrayLength());
            string[] lines = errorWriter.ToString().Split(Environment.NewLine);
            Assert.Single(lines, line => line.StartsWith("Record failed to parse: Line 2", StringComparison.Ordinal));
            Assert.Single(lines, line => line.Contains("Could not parse the 'expected' field", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // The batch p95 is judged against the strictest budget any scored record states, whichever
    // position that record holds and whether or not the others state one. Rows are scored one
    // at a time as they are folded, so the strictest budget has to survive a later record that
    // states a looser one and a record that states none.
    [Fact]
    public async Task RunAsync_RecordsStateDifferentLatencyBudgets_ScorecardUsesTheStrictest()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string content = string.Join(
            Environment.NewLine,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true),
            RecordJson("t2", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true).Replace("\"p95_latency_ms\":2000", "\"p95_latency_ms\":7", StringComparison.Ordinal),
            RecordJson("t3", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true).Replace("\"p95_latency_ms\":2000,", string.Empty, StringComparison.Ordinal),
            RecordJson("t4", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true).Replace("\"p95_latency_ms\":2000", "\"p95_latency_ms\":900", StringComparison.Ordinal));
        await File.WriteAllTextAsync(inputPath, content);
        var outputWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, new StringWriter());
        string evalReportPath = TempFilePath(".txt");

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--eval-report", evalReportPath]);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains(", budget 7 ms: ", outputWriter.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(evalReportPath);
        }
    }

    // D37: cancellation still stops the batch once records run concurrently. The first record
    // cancels the run; the loop starts no record after that, so fewer records compose than the
    // batch holds and the run throws. Rows are written as records finish, so --output may hold
    // the rows folded before the cancel, but never a closed array: a cancelled run must not
    // leave a file that reads as a finished one.
    [Fact]
    public async Task RunAsync_CancelledWhileRecordsAreInFlight_StartsNoFurtherRecordAndThrows()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string[] records = Enumerable.Range(1, 8).Select(number => RecordJson($"t{number}", "2026-01-10", "2025-12-08T15:04:00Z")).ToArray();
        await File.WriteAllTextAsync(inputPath, string.Join(Environment.NewLine, records));
        using var cancellation = new CancellationTokenSource();
        var composer = new CancelOnFirstComposeComposer(cancellation);
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter(), composer);

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(["--input", inputPath, "--output", outputPath], cancellation.Token));

            Assert.InRange(composer.Calls, 1, records.Length - 1);
            string written = await File.ReadAllTextAsync(outputPath);
            Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(written));
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // D10: the run's reference time comes from --now; the send day follows it (A4).
    [Fact]
    public async Task RunAsync_NowFlagProvided_SendAtFollowsTheReferenceDay()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter());

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--now", "2025-12-20T00:00:00-06:00"]);

            Assert.Equal(CliExitCodes.Success, exitCode);
            using JsonDocument output = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            string sendAt = output.RootElement[0].GetProperty("next_message").GetProperty("send_at").GetString()!;
            Assert.StartsWith("2025-12-20T09:00:00-06:00", sendAt);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task RunAsync_NowFlagUnparseable_WritesCleanErrorAndReturnsUsageError()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--now", "yesterday"]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains("--now", errorWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // D1: the diagnostics file names every unknown member and every defaulted decision input.
    [Fact]
    public async Task RunAsync_RecordWithUnknownAndAbsentMembers_DiagnosticsNameThem()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string diagnosticsPath = TempFilePath(".json");
        string line = RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z")
            .Replace("\"language\":\"en\"", "\"language\":\"en\",\"unit\":\"A-204\"")
            .Replace("\"persona\":\"prospect\",", string.Empty);
        await File.WriteAllTextAsync(inputPath, line);
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter());

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--diagnostics", diagnosticsPath]);

            Assert.Equal(CliExitCodes.Success, exitCode);
            using JsonDocument diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(diagnosticsPath));
            JsonElement notes = diagnostics.RootElement[0].GetProperty("ingest_notes");
            Assert.Equal("input.unit", notes.GetProperty("unknown_members")[0].GetString());
            Assert.Equal("persona", notes.GetProperty("defaulted_fields")[0].GetString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(diagnosticsPath);
        }
    }

    // D3 on the wire: a record with no consented channel is a next_message object with
    // channel none, a no_op with its reason, and a diagnostics row naming the suppression.
    [Fact]
    public async Task RunAsync_NoConsentedChannel_WritesNoneMessageNoOpAndSuppressionReason()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string diagnosticsPath = TempFilePath(".json");
        string line = RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z")
            .Replace("\"email_opt_in\":true", "\"email_opt_in\":false")
            .Replace("\"sms_opt_in\":true", "\"sms_opt_in\":false");
        await File.WriteAllTextAsync(inputPath, line);
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter());

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--diagnostics", diagnosticsPath]);

            Assert.Equal(CliExitCodes.Success, exitCode);
            using JsonDocument output = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            JsonElement record = output.RootElement[0];
            Assert.Equal("none", record.GetProperty("next_message").GetProperty("channel").GetString());
            Assert.Equal(JsonValueKind.Null, record.GetProperty("next_message").GetProperty("body").ValueKind);
            Assert.Equal("no_op", record.GetProperty("next_action").GetProperty("type").GetString());
            Assert.Equal("no_contact_consent", record.GetProperty("next_action").GetProperty("reason").GetString());
            using JsonDocument diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(diagnosticsPath));
            Assert.Equal("no_contact_consent", diagnostics.RootElement[0].GetProperty("diagnostics").GetProperty("suppression_reason").GetString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(diagnosticsPath);
        }
    }

    [Fact]
    public async Task RunAsync_OneLineFailsToParse_OtherRecordStillWrittenAndReturnsPartialFailure()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string content = string.Join(
            Environment.NewLine,
            "{not valid json",
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
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
            Assert.Contains("Line 1", errorWriter.ToString());
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
            Assert.Contains("Composer: openai, model gpt-4o-mini.", errorWriter.ToString(), StringComparison.Ordinal);
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
            Assert.Contains("Composer: openai, model gpt-4o.", errorWriter.ToString(), StringComparison.Ordinal);
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
            string[] reportLines = (await File.ReadAllTextAsync(evalReportPath)).Split(Environment.NewLine);
            string labeledRow = Assert.Single(reportLines, line => line.StartsWith("labeled ", StringComparison.Ordinal));
            string unlabeledRow = Assert.Single(reportLines, line => line.StartsWith("unlabeled ", StringComparison.Ordinal));
            Assert.DoesNotContain("ERROR", labeledRow);
            Assert.Contains("OK", labeledRow);
            Assert.Contains("ERROR", unlabeledRow);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(evalReportPath);
        }
    }

    // Closes the "no log file exists anywhere" gap TalkingPoints.md's Sprint 8 audit
    // flagged: --log-file wires Agent.Cli.Logging.FileLoggerProvider into the run, so a
    // real file - not just the plain stderr text every other test in this file asserts
    // against - captures what happened, with the record's TaskId attached via log scope.
    [Fact]
    public async Task RunAsync_LogFilePathProvided_WritesStructuredLogLinesToARealFile()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string logFilePath = TempFilePath(".log");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--log-file", logFilePath]);

            Assert.Equal(CliExitCodes.Success, exitCode);
            string logContent = await File.ReadAllTextAsync(logFilePath);
            Assert.Contains("t1", logContent);
            Assert.Contains("Batch complete", logContent);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            TestFiles.DeleteWithRetry(logFilePath);
        }
    }

    // docs/OPERATIONS.md states the correlation ID is a scope, never a repeated parameter,
    // and that CliRunner's own per-record lines carry TaskId "without needing to pass it
    // explicitly" - this was previously false (CliRunner never opened its own scope). Now
    // that logs route through the injected error TextWriter (ConsoleLoggerProvider), this
    // is directly observable: the rendered line carries TaskId via the scope-rendering
    // "Key=Value" suffix, not embedded in the message text.
    [Fact]
    public async Task RunAsync_RecordProcessed_LogLineCarriesTaskIdViaScopeNotMessageText()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            await runner.RunAsync(["--input", inputPath, "--output", outputPath]);

            string logContent = errorWriter.ToString();
            Assert.Contains("Record processed", logContent);
            Assert.DoesNotContain("Record 't1' processed", logContent);
            Assert.Contains("TaskId=t1", logContent);
            // D16: one scope owner. Two scopes pushing the same key rendered every agent
            // line as "TaskId=t1 TaskId=t1" (the retrospective's logging defect 1).
            Assert.DoesNotContain("TaskId=t1 TaskId=t1", logContent);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // Every other bad-argument case (unknown --composer, missing API key) maps to a clean
    // UsageError + stderr message rather than crashing with a raw unhandled exception -
    // --log-file previously didn't, since FileLoggerProvider's StreamWriter construction
    // ran outside any try/catch.
    [Fact]
    public async Task RunAsync_LogFilePathHasNoParentDirectory_WritesCleanErrorAndReturnsUsageError()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string logFilePath = Path.Combine(Path.GetTempPath(), $"cli-runner-tests-missing-dir-{Guid.NewGuid():N}", "run.log");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--log-file", logFilePath]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains(logFilePath, errorWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // D64: --output gets the guard --log-file already has. An unwritable path ended the
    // process on an unhandled DirectoryNotFoundException with an exit code that is none of
    // the three documented ones (playbook step 79). ThrowingComposer proves the other half,
    // step 77's fail fast before work that costs time or money: it throws for t1, so a run
    // that reached the record loop would say "Injected fault" on stderr and exit 2.
    [Fact]
    public async Task RunAsync_OutputPathHasNoParentDirectory_WritesCleanErrorAndReturnsUsageError()
    {
        string inputPath = TempFilePath();
        string outputPath = Path.Combine(Path.GetTempPath(), $"cli-runner-tests-missing-dir-{Guid.NewGuid():N}", "out.json");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter, new ThrowingComposer("t1"));

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains($"Could not open --output '{outputPath}'", errorWriter.ToString());
            Assert.DoesNotContain("Injected fault", errorWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    // D64, --diagnostics: its own flag in its own message. A guard that named --output for
    // every unwritable path would send an operator to the wrong flag.
    [Fact]
    public async Task RunAsync_DiagnosticsPathHasNoParentDirectory_WritesCleanErrorAndReturnsUsageError()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string diagnosticsPath = Path.Combine(Path.GetTempPath(), $"cli-runner-tests-missing-dir-{Guid.NewGuid():N}", "diag.json");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter, new ThrowingComposer("t1"));

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--diagnostics", diagnosticsPath]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains($"Could not open --diagnostics '{diagnosticsPath}'", errorWriter.ToString());
            Assert.DoesNotContain("Injected fault", errorWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // D64, --review-queue: same guard, its own flag, and still before the record loop.
    [Fact]
    public async Task RunAsync_ReviewQueuePathHasNoParentDirectory_WritesCleanErrorAndReturnsUsageError()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string reviewQueuePath = Path.Combine(Path.GetTempPath(), $"cli-runner-tests-missing-dir-{Guid.NewGuid():N}", "queue.json");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter, new ThrowingComposer("t1"));

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--review-queue", reviewQueuePath]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains($"Could not open --review-queue '{reviewQueuePath}'", errorWriter.ToString());
            Assert.DoesNotContain("Injected fault", errorWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // D64, --eval-report: the report describes the batch, so it is written after the batch
    // and its guard is at the write rather than before the loop. The batch's own output file
    // is still complete; only the report the run asked for could not be written, and that is
    // exit code 1 with one stderr line naming the flag, not a stack trace.
    [Fact]
    public async Task RunAsync_EvalReportPathHasNoParentDirectory_WritesCleanErrorAndReturnsUsageError()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string evalReportPath = Path.Combine(Path.GetTempPath(), $"cli-runner-tests-missing-dir-{Guid.NewGuid():N}", "eval.txt");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true));
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--eval-report", evalReportPath]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains($"Could not open --eval-report '{evalReportPath}'", errorWriter.ToString());
            Assert.Contains("next_message", await File.ReadAllTextAsync(outputPath));
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // D64: --replay writes its scorecard through the same method, so the unwritable
    // --eval-report rule holds there too. A guard that reported the failure and still let the
    // run exit 0 would tell a caller the report is on disk when it is not.
    [Fact]
    public async Task RunAsync_ReplayEvalReportPathHasNoParentDirectory_WritesCleanErrorAndReturnsUsageError()
    {
        string inputPath = TempFilePath();
        string replayPath = TempFilePath(".json");
        string evalReportPath = Path.Combine(Path.GetTempPath(), $"cli-runner-tests-missing-dir-{Guid.NewGuid():N}", "eval.txt");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true));
        await File.WriteAllTextAsync(replayPath, "[{\"next_message\":{\"channel\":\"none\"},\"next_action\":{\"type\":\"no_op\"}}]");
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--replay", replayPath, "--eval-report", evalReportPath]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains($"Could not open --eval-report '{evalReportPath}'", errorWriter.ToString());
            Assert.Contains("Overall:", outputWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(replayPath);
        }
    }

    // D65: --input gets the guard the four output flags got in D64. It opened unguarded, so a
    // path that names no file ended the process on an unhandled FileNotFoundException with a
    // stack trace on stderr and an exit code that is none of the three documented ones. Exit 1
    // and not 2: 2 means some records were processed and some were not, and a file that never
    // opened has no records at all.
    [Fact]
    public async Task RunAsync_InputPathDoesNotExist_WritesCleanErrorAndReturnsUsageError()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"cli-runner-tests-missing-dir-{Guid.NewGuid():N}", "in.jsonl");
        string outputPath = TempFilePath();
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter, new ThrowingComposer("t1"));

        int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath]);

        Assert.Equal(CliExitCodes.UsageError, exitCode);
        Assert.Contains($"Could not open --input '{inputPath}'", errorWriter.ToString());
        Assert.DoesNotContain("   at ", errorWriter.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
    }

    // D65: the second reader path, --replay, opened unguarded in the same way and gets the
    // same guard with its own flag in its own message.
    [Fact]
    public async Task RunAsync_ReplayPathDoesNotExist_WritesCleanErrorAndReturnsUsageError()
    {
        string inputPath = TempFilePath();
        string replayPath = Path.Combine(Path.GetTempPath(), $"cli-runner-tests-missing-dir-{Guid.NewGuid():N}", "out.json");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true));
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--replay", replayPath]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains($"Could not open --replay '{replayPath}'", errorWriter.ToString());
            Assert.DoesNotContain("   at ", errorWriter.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    // D65's second half: an empty value threw ArgumentException, which is not the IOException
    // the open guards filter on, so every path guarded in D64 still ended the process
    // unhandled when its flag was given "". It is closed in the argument parsing instead of by
    // widening a catch filter, because no value any flag of this program takes means anything
    // when empty. The message names the argument the empty one follows, so an operator with
    // six flags on the line knows which one to fix.
    [Fact]
    public async Task RunAsync_OptionGivenAnEmptyValue_WritesCleanErrorAndReturnsUsageError()
    {
        string outputPath = TempFilePath();
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        int exitCode = await runner.RunAsync(["--input", string.Empty, "--output", outputPath]);

        Assert.Equal(CliExitCodes.UsageError, exitCode);
        Assert.Contains("--input", errorWriter.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", errorWriter.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
    }

    // A whitespace-only value is not the empty string the Length check above catches, but it
    // resolves to the same "no real path" fact: Path.GetFullPath throws ArgumentException on
    // it, which is not the IOException the open guards filter on, so it escaped as an
    // unhandled exception the same way "" did before D65 (Antigravity review, PR #26).
    [Fact]
    public async Task RunAsync_OptionGivenAWhitespaceOnlyValue_WritesCleanErrorAndReturnsUsageError()
    {
        string outputPath = TempFilePath();
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        int exitCode = await runner.RunAsync(["--input", "   ", "--output", outputPath]);

        Assert.Equal(CliExitCodes.UsageError, exitCode);
        Assert.Contains("--input", errorWriter.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", errorWriter.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
    }

    // The same rule where there is no preceding argument to name: the position is the only
    // identity the empty argument has.
    [Fact]
    public async Task RunAsync_FirstArgumentIsEmpty_WritesCleanErrorAndReturnsUsageError()
    {
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        int exitCode = await runner.RunAsync([string.Empty, "--input", "in.jsonl", "--output", "out.json"]);

        Assert.Equal(CliExitCodes.UsageError, exitCode);
        Assert.Contains("Argument 1 is empty", errorWriter.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", errorWriter.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_UnknownComposer_LogFileStillWritesTheComposerSelectionFailure()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string logFilePath = TempFilePath(".log");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--composer", "mock", "--log-file", logFilePath]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            string logContent = await File.ReadAllTextAsync(logFilePath);
            Assert.Contains("Composer selection failed", logContent);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            TestFiles.DeleteWithRetry(logFilePath);
        }
    }

    [Fact]
    public async Task RunAsync_RecordThrows_LogFileCapturesTheFailureWithExceptionType()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string logFilePath = TempFilePath(".log");
        string content = string.Join(
            Environment.NewLine,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"),
            RecordJson("t2", "2026-01-10", "2025-12-08T15:04:00Z"));
        await File.WriteAllTextAsync(inputPath, content);
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter, new ThrowingComposer("t2"));

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--log-file", logFilePath]);

            Assert.Equal(CliExitCodes.PartialFailure, exitCode);
            string logContent = await File.ReadAllTextAsync(logFilePath);
            Assert.Contains("t2", logContent);
            Assert.Contains("InvalidOperationException", logContent);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            TestFiles.DeleteWithRetry(logFilePath);
        }
    }

    // Claude Code review of PR #28: a record's own cancellation is not a bug, so it must not be
    // logged as one. LeasingMessageAgent's own catch already excludes OperationCanceledException
    // for that reason; RunRecordAsync's catch did not, so a run cancelled while a record was
    // genuinely in flight (not merely between dispatches, which CancelOnFirstComposeComposer
    // above covers) logged "Record failed." for a clean shutdown.
    [Fact]
    public async Task RunAsync_RecordCancelledMidFlight_LogFileDoesNotRecordItAsAFailure()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string logFilePath = TempFilePath(".log");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        using var cancellation = new CancellationTokenSource();
        var composer = new CancelsWhileComposingComposer(cancellation);
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter(), composer);

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => runner.RunAsync(["--input", inputPath, "--output", outputPath, "--log-file", logFilePath], cancellation.Token));

            string logContent = await File.ReadAllTextAsync(logFilePath);
            Assert.DoesNotContain("Record failed.", logContent);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            TestFiles.DeleteWithRetry(logFilePath);
        }
    }

    // D14: --replay re-scores an existing output file against --input without running the
    // agent. The composer injected here throws on the only record, so a run that reached
    // the agent would exit 2; exit 0 proves nothing ran but the scorer.
    [Fact]
    public async Task RunAsync_Replay_ScoresAnExistingOutputFileWithoutRunningTheAgent()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string evalReportPath = TempFilePath(".txt");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true));
        var producer = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter());
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var replayer = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter, new ThrowingComposer("t1"));

        try
        {
            Assert.Equal(CliExitCodes.Success, await producer.RunAsync(["--input", inputPath, "--output", outputPath]));

            int exitCode = await replayer.RunAsync(["--input", inputPath, "--replay", outputPath, "--eval-report", evalReportPath]);

            Assert.Equal(CliExitCodes.Success, exitCode);
            string report = await File.ReadAllTextAsync(evalReportPath);
            string row = Assert.Single(report.Split(Environment.NewLine), line => line.StartsWith("t1 ", StringComparison.Ordinal));
            Assert.Contains("n/a", row);
            Assert.Contains("Latency p95: n/a", report);
            Assert.Contains("Overall:", outputWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(evalReportPath);
        }
    }

    [Fact]
    public async Task RunAsync_ReplayWithoutEvalReport_PrintsTheScorecardToTheConsole()
    {
        string inputPath = TempFilePath();
        string replayPath = TempFilePath(".json");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true));
        await File.WriteAllTextAsync(replayPath, "[{\"next_message\":{\"channel\":\"none\"},\"next_action\":{\"type\":\"no_op\"}}]");
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--replay", replayPath]);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("Overall: 0/1 passed", outputWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(replayPath);
        }
    }

    [Fact]
    public async Task RunAsync_ReplayCountMismatch_WritesCleanErrorAndReturnsUsageError()
    {
        string inputPath = TempFilePath();
        string replayPath = TempFilePath(".json");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true));
        await File.WriteAllTextAsync(replayPath, "[]");
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--replay", replayPath]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains("1 record", errorWriter.ToString());
            Assert.Contains("0 output", errorWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(replayPath);
        }
    }

    [Fact]
    public async Task RunAsync_ReplayFileNotAJsonArray_WritesCleanErrorAndReturnsUsageError()
    {
        string inputPath = TempFilePath();
        string replayPath = TempFilePath(".json");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true));
        await File.WriteAllTextAsync(replayPath, "{not json");
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--replay", replayPath]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains("JSON array", errorWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(replayPath);
        }
    }


    [Fact]
    public async Task RunAsync_InputWithoutOutputOrReplay_WritesUsageAndReturnsUsageError()
    {
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        int exitCode = await runner.RunAsync(["--input", "anything.jsonl"]);

        Assert.Equal(CliExitCodes.UsageError, exitCode);
        Assert.Contains("--replay", errorWriter.ToString());
    }

    // --output and --replay select mutually exclusive modes (D14); passing both used to
    // silently run --replay and never write --output, with no diagnostic.
    [Fact]
    public async Task RunAsync_OutputAndReplayBothGiven_WritesUsageAndReturnsUsageError()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string replayPath = TempFilePath(".json");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true));
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--replay", replayPath]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains("--output", errorWriter.ToString());
            Assert.Contains("--replay", errorWriter.ToString());
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    // The total in "Batch complete" must count records read, not records read plus
    // processing failures: a record that throws stays in `cases` (per-record isolation)
    // and previously got added into the total a second time via failureCount.
    [Fact]
    public async Task RunAsync_OneRecordThrows_BatchCompleteReportsTheRecordsReadNotDoubleCounted()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string logFilePath = TempFilePath(".log");
        string content = string.Join(
            Environment.NewLine,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"),
            RecordJson("t2", "2026-01-10", "2025-12-08T15:04:00Z"));
        await File.WriteAllTextAsync(inputPath, content);
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter(), new ThrowingComposer("t2"));

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--log-file", logFilePath]);

            Assert.Equal(CliExitCodes.PartialFailure, exitCode);
            string logContent = await File.ReadAllTextAsync(logFilePath);
            // The elapsed and the model cost D61 and D62 added to this line follow the counts,
            // which are what this test is about; the comma is where they start.
            Assert.Contains("Batch complete: 2 record(s), 1 failure(s),", logContent);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            TestFiles.DeleteWithRetry(logFilePath);
        }
    }

    // D30: the judge is off unless --judge is passed, and it needs the same key the model
    // composer does. An empty input file builds it and scores nothing, so no call is made.
    [Fact]
    public async Task RunAsync_JudgeRequestedWithApiKeyAndNoRecords_ScoresWithoutCallingTheModel()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string reportPath = TempFilePath(".txt");
        await File.WriteAllTextAsync(inputPath, string.Empty);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new("OpenAI:ApiKey", "fake-key-for-coverage")])
            .Build();
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(configuration, outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--eval-report", reportPath, "--judge"]);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("ActionSem", await File.ReadAllTextAsync(reportPath));
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(reportPath);
        }
    }

    // Playbook step 77: bad configuration fails before any work, and a judge with no key is
    // bad configuration, not a run that quietly scores nothing.
    [Fact]
    public async Task RunAsync_JudgeRequestedWithoutApiKey_WritesCleanErrorAndReturnsUsageError()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--judge"]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains("--judge needs OpenAI:ApiKey", errorWriter.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // Both prior judge tests process an empty batch, so SemanticJudge.JudgeAsync's
    // per-record grading loop never actually ran through CliRunner - only in isolation
    // (SemanticJudgeTests). judgeOverride closes that gap the way composerOverride already
    // does for the composer path: a real record, scored by a real Evaluator, graded by a
    // judge this test controls, so a regression in how CliRunner wires the two together
    // (wrong list, dropped record, broken ordering) would show up here.
    [Fact]
    public async Task RunAsync_JudgeRequestedWithRecords_GradesThemUsingTheProvidedJudge()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string reportPath = TempFilePath(".txt");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true));
        var fakeJudgeClient = new FixedJudgeCompletionClient("""{"action_matches":true,"body_matches":true,"reason":"matches"}""");
        var judge = new SemanticJudge(fakeJudgeClient);
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), outputWriter, errorWriter, judgeOverride: judge);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--eval-report", reportPath, "--judge"]);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Equal(1, fakeJudgeClient.CallCount);
            string report = await File.ReadAllTextAsync(reportPath);
            Assert.Contains("ActionSem 1/1", report);
            Assert.Contains("BodySem 1/1", report);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(reportPath);
        }
    }

    // A record whose own city_interest is "families only" has that text written into its
    // body by the template composer ("We heard you're looking in families only."), so every
    // compose attempt and the fallback are refused by the safety gate. Before D48's seam
    // change the refusal destroyed the draft: this run reported composition_failed, recorded
    // fair_housing_check_passed as not_evaluated, and could queue nothing. All three are
    // asserted here, through the real CLI wiring, because that wiring is what made
    // SuppressionReason.SafetyViolation unreachable.
    [Fact]
    public async Task RunAsync_SafetyGateRefusesEveryDraft_RecordsASafetyViolationAndQueuesTheDraft()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string diagnosticsPath = TempFilePath(".json");
        string reviewQueuePath = TempFilePath(".json");
        string line = SteeringRecordJson("t1");
        await File.WriteAllTextAsync(inputPath, line);
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter());

        try
        {
            int exitCode = await runner.RunAsync(
                ["--input", inputPath, "--output", outputPath, "--diagnostics", diagnosticsPath, "--review-queue", reviewQueuePath]);

            Assert.Equal(CliExitCodes.Success, exitCode);

            using JsonDocument diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(diagnosticsPath));
            JsonElement row = diagnostics.RootElement[0].GetProperty("diagnostics");
            Assert.Equal("safety_violation", row.GetProperty("suppression_reason").GetString());
            Assert.Equal("not_earned", row.GetProperty("required_states").GetProperty("fair_housing_check_passed").GetString());

            using JsonDocument queue = JsonDocument.Parse(await File.ReadAllTextAsync(reviewQueuePath));
            Assert.Equal(1, queue.RootElement.GetArrayLength());
            JsonElement entry = queue.RootElement[0];
            Assert.Equal("t1", entry.GetProperty("task_id").GetString());
            Assert.Equal("sms", entry.GetProperty("draft").GetProperty("channel").GetString());
            Assert.Contains("families only", entry.GetProperty("draft").GetProperty("body").GetString()!, StringComparison.Ordinal);
            Assert.Equal("fair_housing", entry.GetProperty("violations")[0].GetProperty("check").GetString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(diagnosticsPath);
            File.Delete(reviewQueuePath);
        }
    }

    // D43: the file is written even when nothing was queued, because a missing file cannot
    // be told apart from a flag nobody passed, and an empty queue is the number step 71 asks
    // for. A consent-suppressed record is in the same run to prove the second half of the
    // rule: not contactable is the correct decision, so it is never a queue row.
    [Fact]
    public async Task RunAsync_NothingSuppressedBySafety_WritesAnEmptyQueueFile()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string reviewQueuePath = TempFilePath(".json");
        string noConsent = RecordJson("t2", "2026-01-10", "2025-12-08T15:04:00Z")
            .Replace("\"email_opt_in\":true", "\"email_opt_in\":false", StringComparison.Ordinal)
            .Replace("\"sms_opt_in\":true", "\"sms_opt_in\":false", StringComparison.Ordinal);
        string content = string.Join(
            Environment.NewLine,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"),
            noConsent);
        await File.WriteAllTextAsync(inputPath, content);
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter());

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--review-queue", reviewQueuePath]);

            Assert.Equal(CliExitCodes.Success, exitCode);
            using JsonDocument queue = JsonDocument.Parse(await File.ReadAllTextAsync(reviewQueuePath));
            Assert.Equal(0, queue.RootElement.GetArrayLength());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(reviewQueuePath);
        }
    }

    // D43: a queued record leaves the run at exit 0. Suppression is a correct pipeline
    // outcome, not a processing failure, and exit 2 would tell an operator the batch broke.
    [Fact]
    public async Task RunAsync_QueuedRecordAndACleanRecord_StillReturnsSuccess()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string reviewQueuePath = TempFilePath(".json");
        string content = string.Join(
            Environment.NewLine,
            SteeringRecordJson("t1"),
            RecordJson("t2", "2026-01-10", "2025-12-08T15:04:00Z"));
        await File.WriteAllTextAsync(inputPath, content);
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--review-queue", reviewQueuePath]);

            Assert.Equal(CliExitCodes.Success, exitCode);
            using JsonDocument output = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            Assert.Equal(2, output.RootElement.GetArrayLength());
            using JsonDocument queue = JsonDocument.Parse(await File.ReadAllTextAsync(reviewQueuePath));
            Assert.Equal(1, queue.RootElement.GetArrayLength());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(reviewQueuePath);
        }
    }

    // A record no catalog row covers still gets its message, and also a queue row naming the
    // persona and stage that had no row and the action the generic row gave it. A record that
    // is also refused on safety gets one row with both reasons. The run still exits 0: a
    // queued record is work for a person, not a failed record.
    [Fact]
    public async Task RunAsync_GenericRowAnswers_QueuesARowPerRecordWithEveryReason()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string reviewQueuePath = TempFilePath(".json");
        const string CatalogPair = "\"persona\":\"prospect\",\"lifecycle_stage\":\"new\"";
        const string UnseenPair = "\"persona\":\"guarantor\",\"lifecycle_stage\":\"screening\"";
        string content = string.Join(
            Environment.NewLine,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"),
            RecordJson("t2", "2026-01-10", "2025-12-08T15:04:00Z").Replace(CatalogPair, UnseenPair, StringComparison.Ordinal),
            SteeringRecordJson("t3").Replace(CatalogPair, UnseenPair, StringComparison.Ordinal));
        await File.WriteAllTextAsync(inputPath, content);
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter());

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--review-queue", reviewQueuePath]);

            Assert.Equal(CliExitCodes.Success, exitCode);

            using JsonDocument output = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            Assert.Equal("sms", output.RootElement[1].GetProperty("next_message").GetProperty("channel").GetString());

            using JsonDocument queue = JsonDocument.Parse(await File.ReadAllTextAsync(reviewQueuePath));
            Assert.Equal(2, queue.RootElement.GetArrayLength());

            JsonElement genericOnly = queue.RootElement[0];
            Assert.Equal("t2", genericOnly.GetProperty("task_id").GetString());
            Assert.Equal(new[] { "generic_row_no_match" }, genericOnly.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()).ToArray());
            Assert.Equal(JsonValueKind.Null, genericOnly.GetProperty("draft").ValueKind);
            JsonElement genericRow = genericOnly.GetProperty("generic_row");
            Assert.Equal("guarantor", genericRow.GetProperty("persona").GetString());
            Assert.Equal("screening", genericRow.GetProperty("lifecycle_stage").GetString());
            Assert.True(JsonElement.DeepEquals(output.RootElement[1].GetProperty("next_action"), genericRow.GetProperty("action")));

            JsonElement both = queue.RootElement[1];
            Assert.Equal("t3", both.GetProperty("task_id").GetString());
            Assert.Equal(
                new[] { "safety_violation", "generic_row_no_match" },
                both.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()).ToArray());
            Assert.Equal("fair_housing", both.GetProperty("violations")[0].GetProperty("check").GetString());
            Assert.Equal("guarantor", both.GetProperty("generic_row").GetProperty("persona").GetString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(reviewQueuePath);
        }
    }

    // D43 and D14: --replay runs no validator at all, so it has nothing to queue. Asking for
    // a queue from a replay is a usage error rather than a silently empty file that reads as
    // a clean run.
    [Fact]
    public async Task RunAsync_ReviewQueueWithReplay_WritesCleanErrorAndReturnsUsageError()
    {
        string inputPath = TempFilePath();
        string replayPath = TempFilePath(".json");
        string reviewQueuePath = TempFilePath(".json");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z"));
        await File.WriteAllTextAsync(replayPath, "[]");
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), errorWriter);

        try
        {
            int exitCode = await runner.RunAsync(["--input", inputPath, "--replay", replayPath, "--review-queue", reviewQueuePath]);

            Assert.Equal(CliExitCodes.UsageError, exitCode);
            Assert.Contains("--review-queue", errorWriter.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(reviewQueuePath));
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(replayPath);
            File.Delete(reviewQueuePath);
        }
    }

    // D61: the per-record number is the wall-clock elapsed of exactly one RunAsync call, and
    // it is on the diagnostics row from now on. Shape and presence only: it is wall clock and
    // moves between runs on unchanged code (DESIGN.md section 9), so no test pins a
    // millisecond count.
    [Fact]
    public async Task RunAsync_DiagnosticsPathProvided_EveryRowCarriesItsOwnLatency()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string diagnosticsPath = TempFilePath(".json");
        await File.WriteAllTextAsync(
            inputPath,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z") + Environment.NewLine +
            RecordJson("t2", "2026-01-11", "2025-12-08T16:04:00Z"));
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter());

        try
        {
            await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--diagnostics", diagnosticsPath]);

            using JsonDocument diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(diagnosticsPath));
            Assert.Equal(2, diagnostics.RootElement.GetArrayLength());

            foreach (JsonElement row in diagnostics.RootElement.EnumerateArray())
            {
                JsonElement latency = row.GetProperty("latency_ms");
                Assert.Equal(JsonValueKind.Number, latency.ValueKind);
                Assert.True(latency.GetDouble() >= 0);
            }
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(diagnosticsPath);
        }
    }

    // D61 option (c): one measurement with two readers. The diagnostics row and the scorecard
    // row state the same number for the same record, because the loop hands both the same
    // variable. Two stopwatches around the same call would disagree here.
    [Fact]
    public async Task RunAsync_DiagnosticsAndEvalReport_StateTheSameLatencyForEachRecord()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string diagnosticsPath = TempFilePath(".json");
        string evalReportPath = TempFilePath(".txt");
        await File.WriteAllTextAsync(
            inputPath,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true) + Environment.NewLine +
            RecordJson("t2", "2026-01-11", "2025-12-08T16:04:00Z", includeExpected: true));
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter());

        try
        {
            await runner.RunAsync(
                ["--input", inputPath, "--output", outputPath, "--diagnostics", diagnosticsPath, "--eval-report", evalReportPath]);

            string[] reportLines = (await File.ReadAllTextAsync(evalReportPath)).Split(Environment.NewLine);
            using JsonDocument diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(diagnosticsPath));

            foreach (JsonElement row in diagnostics.RootElement.EnumerateArray())
            {
                string taskId = row.GetProperty("task_id").GetString()!;
                string scored = Assert.Single(reportLines, line => line.StartsWith(taskId + " ", StringComparison.Ordinal));
                string[] cells = scored.Split('|');

                Assert.Equal(
                    row.GetProperty("latency_ms").GetDouble().ToString("0", CultureInfo.InvariantCulture),
                    cells[^2].Trim());
            }
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(diagnosticsPath);
            File.Delete(evalReportPath);
        }
    }

    // D61, per batch: one wall-clock elapsed around the record loop, on the two artifacts that
    // are already per batch. It is not a row in the diagnostics array, which stays one row per
    // unit of work. The pattern pins the shape and never a duration.
    [Fact]
    public async Task RunAsync_AnyBatch_ReportsTheBatchElapsedOnTheLogLineAndTheScorecard()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string evalReportPath = TempFilePath(".txt");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true));
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), errorWriter);

        try
        {
            await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--eval-report", evalReportPath]);

            Assert.Matches(@"Batch complete: 1 record\(s\), 0 failure\(s\), [0-9.]+ms elapsed", errorWriter.ToString());
            Assert.Matches(@"Batch latency: [0-9]+ ms", await File.ReadAllTextAsync(evalReportPath));
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(evalReportPath);
        }
    }

    // D62, the first state through the whole program: the default composer issues no request,
    // so every row states no measurement and the batch has none to total.
    [Fact]
    public async Task RunAsync_TemplateComposer_BatchLineAndScorecardStateNoModelCall()
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string diagnosticsPath = TempFilePath(".json");
        string evalReportPath = TempFilePath(".txt");
        await File.WriteAllTextAsync(inputPath, RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true));
        var errorWriter = new StringWriter();
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), errorWriter);

        try
        {
            await runner.RunAsync(
                ["--input", inputPath, "--output", outputPath, "--diagnostics", diagnosticsPath, "--eval-report", evalReportPath]);

            using JsonDocument diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(diagnosticsPath));
            JsonElement row = diagnostics.RootElement[0].GetProperty("diagnostics");

            Assert.Equal(JsonValueKind.Null, row.GetProperty("model_cost").ValueKind);
            Assert.Contains("model cost none.", errorWriter.ToString(), StringComparison.Ordinal);
            Assert.Contains("Batch model cost: none", await File.ReadAllTextAsync(evalReportPath), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(diagnosticsPath);
            File.Delete(evalReportPath);
        }
    }

    // D62, the third state through the whole program, and the batch total over it: two records,
    // one completed call each, and a batch line that adds them up. The client is fake, so no
    // network call and no key are involved; what is real is the composer, the compose-validate
    // loop, the agent and the writer the counts travel through.
    [Fact]
    public async Task RunAsync_ModelComposerWithCompletedCalls_TotalsTheTokensEveryRecordSpent()
    {
        const string completionJson = """{"subject":null,"body":"Hi Taylor! Book a tour. Reply STOP to opt out.","cta_type":"schedule_tour","cta_options":["Thu","Fri"]}""";
        string inputPath = TempFilePath();
        string outputPath = TempFilePath();
        string diagnosticsPath = TempFilePath(".json");
        string evalReportPath = TempFilePath(".txt");
        await File.WriteAllTextAsync(
            inputPath,
            RecordJson("t1", "2026-01-10", "2025-12-08T15:04:00Z", includeExpected: true) + Environment.NewLine +
            RecordJson("t2", "2026-01-11", "2025-12-08T16:04:00Z", includeExpected: true));
        var errorWriter = new StringWriter();
        var composer = new OpenAiMessageComposer(new CostingCompletionClient(completionJson, inputTokens: 11, outputTokens: 7));
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), errorWriter, composer);

        try
        {
            await runner.RunAsync(
                ["--input", inputPath, "--output", outputPath, "--diagnostics", diagnosticsPath, "--eval-report", evalReportPath]);

            using JsonDocument diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(diagnosticsPath));
            JsonElement modelCost = diagnostics.RootElement[0].GetProperty("diagnostics").GetProperty("model_cost");

            Assert.Equal(1, modelCost.GetProperty("calls").GetInt32());
            Assert.Equal(1, modelCost.GetProperty("completed_calls").GetInt32());
            Assert.Equal(11, modelCost.GetProperty("input_tokens").GetInt32());
            Assert.Equal(7, modelCost.GetProperty("output_tokens").GetInt32());

            const string total = "2 call(s), 2 completed, 22 input + 14 output token(s)";
            Assert.Contains("model cost " + total + ".", errorWriter.ToString(), StringComparison.Ordinal);
            Assert.Contains("Batch model cost: " + total, await File.ReadAllTextAsync(evalReportPath), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(diagnosticsPath);
            File.Delete(evalReportPath);
        }
    }

    // D66 through the whole program, on D48's steering record. The model answers both
    // attempts with a body that repeats the record's own steering language, so both are
    // rejected on safety; the template fallback writes the same language out of the record's
    // own city_interest and is refused too. Nothing ships, so the record has no composition
    // notes - and the two completed calls the vendor billed for are on the row beside its
    // latency, where a suppression cannot take them with it.
    [Fact]
    public async Task RunAsync_SuppressedRecordThatCalledTheModel_KeepsTheCostOnItsDiagnosticsRow()
    {
        const string steeringCompletionJson = """{"subject":null,"body":"This community is families only. Reply STOP to opt out.","cta_type":"schedule_tour","cta_options":["Thu","Fri"]}""";
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string diagnosticsPath = TempFilePath(".json");
        await File.WriteAllTextAsync(inputPath, SteeringRecordJson("t1"));
        var composer = new OpenAiMessageComposer(new CostingCompletionClient(steeringCompletionJson, inputTokens: 11, outputTokens: 7));
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), new StringWriter(), composer);

        try
        {
            await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--diagnostics", diagnosticsPath]);

            using JsonDocument diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(diagnosticsPath));
            JsonElement row = diagnostics.RootElement[0].GetProperty("diagnostics");

            Assert.Equal("safety_violation", row.GetProperty("suppression_reason").GetString());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("composition").ValueKind);

            JsonElement modelCost = row.GetProperty("model_cost");
            Assert.Equal(2, modelCost.GetProperty("calls").GetInt32());
            Assert.Equal(2, modelCost.GetProperty("completed_calls").GetInt32());
            Assert.Equal(22, modelCost.GetProperty("input_tokens").GetInt32());
            Assert.Equal(14, modelCost.GetProperty("output_tokens").GetInt32());
            Assert.Equal(0, row.GetProperty("network_retries").GetInt32());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(diagnosticsPath);
        }
    }

    // The second rule the same record proves: the batch total is summed off the rows the loop
    // writes, so a record whose cost survives its suppression changes the batch number too.
    // Before D66 both the row and the total read as a run that never called a model.
    [Fact]
    public async Task RunAsync_SuppressedRecordThatCalledTheModel_CountsTowardTheBatchTotal()
    {
        const string steeringCompletionJson = """{"subject":null,"body":"This community is families only. Reply STOP to opt out.","cta_type":"schedule_tour","cta_options":["Thu","Fri"]}""";
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        string evalReportPath = TempFilePath(".txt");
        await File.WriteAllTextAsync(inputPath, SteeringRecordJson("t1"));
        var errorWriter = new StringWriter();
        var composer = new OpenAiMessageComposer(new CostingCompletionClient(steeringCompletionJson, inputTokens: 11, outputTokens: 7));
        var runner = new CliRunner(EmptyConfiguration(), new StringWriter(), errorWriter, composer);

        try
        {
            await runner.RunAsync(["--input", inputPath, "--output", outputPath, "--eval-report", evalReportPath]);

            const string total = "2 call(s), 2 completed, 22 input + 14 output token(s)";
            Assert.Contains("model cost " + total + ".", errorWriter.ToString(), StringComparison.Ordinal);
            Assert.Contains("Batch model cost: " + total, await File.ReadAllTextAsync(evalReportPath), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            File.Delete(evalReportPath);
        }
    }
}
