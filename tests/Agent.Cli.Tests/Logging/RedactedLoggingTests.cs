using System.ClientModel;
using System.Net;
using Agent.Cli.Logging;
using Agent.Common;
using Agent.Composition;
using Agent.Domain;
using Agent.Evaluation;
using Agent.Ingest;
using Agent.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Agent.Cli.Tests.Logging;

// Playbook step 68: no vendor error body, no fragment of a model response, and no text a
// prospect record authored reaches a log line above debug level.
//
// Every assertion here is against the rendered line, not against a captured LogEntry.
// LogLineFormatter appends the whole Exception.ToString() after the message, so a leak
// carried by an exception attached to the entry is invisible to a test that reads only
// LogEntry.Message. RenderedLine below is the same sink the CLI writes to in production
// (ConsoleLoggerProvider over the injected error writer).
public class RedactedLoggingTests
{
    // The body a vendor returns with an error status. The SDK copies it verbatim into
    // ClientResultException.Message, so it is in the exception whether or not it reaches
    // the log; VendorErrorBody_LoggedWithTheExceptionAttached_DoesReachTheRenderedLine
    // below is the control that proves exactly that.
    private const string VendorBodyMarker = "VENDOR-BODY-MARKER";

    private const string VendorErrorBody =
        "{\"error\":{\"message\":\"" + VendorBodyMarker + " for key sk-live-abc\"}}";

    // A completion whose content is not valid JSON, malformed at a property name the model
    // authored: the deserializer's own message and path quote that name back.
    private const string ModelAuthoredMarker = "model_wrote_taylor_quinn";

    private const string MalformedPayloadCompletion =
        "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"gpt-4o-mini\"," +
        "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":" +
        "\"{\\\"body\\\":\\\"hi\\\",\\\"" + ModelAuthoredMarker + "\\\":Taylor}\"},\"finish_reason\":\"stop\"}]}";

    private const string RecordLine =
        "{\"task_id\":\"t1\",\"persona\":\"prospect\",\"lifecycle_stage\":\"new\"," +
        "\"consent\":{\"email_opt_in\":true,\"sms_opt_in\":true,\"voice_opt_in\":false}," +
        "\"channel_preferences\":[\"sms\"]," +
        "\"input\":{\"property_name\":\"Oak Ridge\",\"move_date_target\":\"2026-01-10\",\"last_interaction\":\"2025-12-08T15:04:00Z\",\"timezone\":\"America/Chicago\",\"language\":\"en\",\"profile\":{\"first_name\":\"Taylor\"}}," +
        "\"assertions\":{\"required_states\":[],\"constraints\":{\"no_pii_leak\":true,\"include_opt_out_instructions\":true,\"primary_cta\":\"book_tour\"}}," +
        "\"thresholds\":{\"p95_latency_ms\":2000,\"personalization_score_min\":0.85,\"reply_classification_f1_min\":0.9,\"safety_violations_max\":0}}";

    // The judge only calls the model for a record that carries a label to grade against.
    private static readonly string LabeledRecordLine = RecordLine[..^1] +
        ",\"expected\":{\"next_message\":{\"channel\":\"sms\",\"body\":\"hi\"},\"next_action\":{\"type\":\"start_cadence\"}}}";

    private static ProspectCase ParseRecord(string line) =>
        new JsonlRecordReader().ReadAll(new StringReader(line))[0].Value;

    private static ILoggerFactory RenderingFactory(TextWriter writer) =>
        LoggerFactory.Create(builder => builder.AddProvider(new ConsoleLoggerProvider(writer)));

    private static string TempFilePath(string extension = ".jsonl") =>
        Path.Combine(Path.GetTempPath(), $"redacted-logging-tests-{Guid.NewGuid():N}{extension}");

    // Runs one CLI batch over the given input lines and returns everything the CLI wrote to
    // its error writer, which is where ConsoleLoggerProvider renders every log line.
    private static async Task<string> RenderedCliRun(string inputContent)
    {
        string inputPath = TempFilePath();
        string outputPath = TempFilePath(".json");
        await File.WriteAllTextAsync(inputPath, inputContent);
        var errorWriter = new StringWriter();
        var runner = new CliRunner(new ConfigurationBuilder().Build(), new StringWriter(), errorWriter);

        try
        {
            await runner.RunAsync(["--input", inputPath, "--output", outputPath]);
            return errorWriter.ToString();
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    // Finding 1, the composer: a non-success status raises ClientResultException, whose
    // Message is the vendor's raw response body.
    [Fact]
    public async Task ComposeAsync_VendorErrorStatus_RenderedLineCarriesTheStatusAndNotTheBody()
    {
        var writer = new StringWriter();
        var handler = new FakeHttpMessageHandler((HttpStatusCode.Unauthorized, VendorErrorBody));
        using var httpClient = new HttpClient(handler);
        using ILoggerFactory factory = RenderingFactory(writer);
        var composer = new OpenAiMessageComposer(
            new OpenAiCompletionClient(httpClient, "fake-key"),
            factory.CreateLogger<OpenAiMessageComposer>());

        ComposeOutcome.Failed result = Assert.IsType<ComposeOutcome.Failed>(
            await composer.ComposeAsync(ParseRecord(RecordLine), CommunicationChannel.Sms));

        string rendered = writer.ToString();
        Assert.DoesNotContain(VendorBodyMarker, rendered);
        Assert.DoesNotContain("sk-live-abc", rendered);
        Assert.Contains("ClientResultException", rendered);
        Assert.Contains("401", rendered);

        // The same failure travels on as ComposeOutcome.Failed, which ValidatingMessageComposer
        // and LeasingMessageAgent both log downstream; redacting only the composer's own line
        // would move the leak rather than close it.
        Assert.DoesNotContain(VendorBodyMarker, result.Error);
    }

    // The negative control for every DoesNotContain above. It uses the same sink, the same
    // exception, and the same marker, logged the way the composer used to log it. If this
    // fails, the assertions above prove nothing: either the vendor body never reached the
    // exception, or the rendered line is not being read.
    [Fact]
    public async Task VendorError_LoggedWithTheExceptionAttached_DoesReachTheRenderedLine()
    {
        var writer = new StringWriter();
        var handler = new FakeHttpMessageHandler((HttpStatusCode.Unauthorized, VendorErrorBody));
        using var httpClient = new HttpClient(handler);
        using ILoggerFactory factory = RenderingFactory(writer);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");

        ClientResultException vendorFailure =
            await Assert.ThrowsAsync<ClientResultException>(() => client.CompleteAsync("system", "user"));
        factory.CreateLogger<OpenAiMessageComposer>().LogWarning(vendorFailure, "Completion request failed.");

        Assert.Contains(VendorBodyMarker, writer.ToString());
    }

    // Finding 2, the composer: the model's own output fails to deserialize, and the
    // deserializer's message and path name the member the model wrote.
    [Fact]
    public async Task ComposeAsync_ModelResponseNotValidJson_RenderedLineCarriesNoFragmentOfIt()
    {
        var writer = new StringWriter();
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, MalformedPayloadCompletion));
        using var httpClient = new HttpClient(handler);
        using ILoggerFactory factory = RenderingFactory(writer);
        var composer = new OpenAiMessageComposer(
            new OpenAiCompletionClient(httpClient, "fake-key"),
            factory.CreateLogger<OpenAiMessageComposer>());

        ComposeOutcome.Failed result = Assert.IsType<ComposeOutcome.Failed>(
            await composer.ComposeAsync(ParseRecord(RecordLine), CommunicationChannel.Sms));

        string rendered = writer.ToString();
        Assert.DoesNotContain(ModelAuthoredMarker, rendered);
        Assert.DoesNotContain(ModelAuthoredMarker, result.Error);
        Assert.Contains("JsonException", rendered);
    }

    // Finding 1, the judge: the same catch, on the evaluation path.
    [Fact]
    public async Task JudgeAsync_VendorErrorStatus_RenderedLineCarriesNoBody()
    {
        var writer = new StringWriter();
        var handler = new FakeHttpMessageHandler((HttpStatusCode.Unauthorized, VendorErrorBody));
        using var httpClient = new HttpClient(handler);
        using ILoggerFactory factory = RenderingFactory(writer);
        var run = new ScoredRun(
            ParseRecord(LabeledRecordLine),
            new AgentOutput(new NextMessage(CommunicationChannel.Sms, null, null, "hi"), new NextAction("start_cadence")),
            0,
            12.5);
        Scorecard scorecard = new Evaluator().Evaluate([run]);
        var judge = new SemanticJudge(
            new OpenAiCompletionClient(httpClient, "fake-key"),
            factory.CreateLogger<SemanticJudge>());

        await judge.JudgeAsync(scorecard, [run]);

        string rendered = writer.ToString();
        Assert.DoesNotContain(VendorBodyMarker, rendered);
        Assert.DoesNotContain("sk-live-abc", rendered);
        Assert.Contains("ClientResultException", rendered);
    }

    // Finding 5: the parse error names the line, not what was on it. The offending member
    // here is one the record types do not declare, so the deserializer's own JSON path is
    // record-authored text, not a schema name.
    [Fact]
    public async Task RunAsync_MalformedRecord_RenderedFailureRowNamesTheLineAndNoRecordText()
    {
        const string undeclaredMember = "note_from_taylor_quinn";
        string malformed = "{\"task_id\":\"t2\",\"consent\":{},\"channel_preferences\":[\"sms\"],\"" + undeclaredMember + "\":Taylor}";

        string rendered = await RenderedCliRun(RecordLine + Environment.NewLine + malformed + Environment.NewLine);

        Assert.Contains("Line 2 failed to parse", rendered);
        Assert.DoesNotContain(undeclaredMember, rendered);
        Assert.DoesNotContain("'T' is an invalid start of a value", rendered);
    }

    // Finding 5, the shape the audit named: a line cut off inside a prospect's first name.
    [Fact]
    public async Task RunAsync_RecordTruncatedInsideAName_RenderedFailureRowNamesTheLineAndNotTheName()
    {
        const string truncated =
            "{\"task_id\":\"t2\",\"consent\":{},\"channel_preferences\":[\"sms\"],\"input\":{\"profile\":{\"first_name\":\"Taylo";

        string rendered = await RenderedCliRun(RecordLine + Environment.NewLine + truncated + Environment.NewLine);

        Assert.Contains("Line 2 failed to parse", rendered);
        Assert.Contains("JsonException", rendered);
        Assert.DoesNotContain("Taylo", rendered);
        Assert.DoesNotContain("Expected end of string", rendered);
    }

    // Finding 4: the ingest line runs on every record of every run, so the two members a
    // record authors - the timezone id it stated and the names of members the schema does
    // not declare - are the two that must not be in it. Which schema fields defaulted still
    // is: those names are this program's own.
    [Fact]
    public async Task RunAsync_UnrecognizedTimezoneAndUndeclaredMember_NeitherReachesTheIngestLine()
    {
        const string timeZoneMarker = "Not/A_Zone_Named_Quinn";
        const string undeclaredMember = "note_from_taylor_quinn";
        string record = RecordLine
            .Replace("America/Chicago", timeZoneMarker)
            .Replace("\"task_id\":\"t1\"", "\"task_id\":\"t1\",\"" + undeclaredMember + "\":\"call after 5\"");

        string rendered = await RenderedCliRun(record + Environment.NewLine);

        Assert.Contains("Ingest:", rendered);
        Assert.Contains("input.timezone", rendered);
        Assert.DoesNotContain(timeZoneMarker, rendered);
        Assert.DoesNotContain(undeclaredMember, rendered);
    }

    // Finding 6: the labeled `expected` block is the one part of a record that holds a
    // written message. Its parse failure is reported as a type and a position, with no
    // exception attached, so no stack trace and no parser prose about the value reach the
    // line either.
    [Fact]
    public async Task RunAsync_ExpectedBlockFailsToParse_RenderedLineCarriesNoExceptionDetail()
    {
        const string labelMarker = "LABEL-BODY-MARKER";
        string record = RecordLine.Replace(
            "\"thresholds\":{",
            "\"expected\":{\"next_action\":{\"type\":\"start_cadence\"},\"next_message\":{\"channel\":\"sms\",\"body\":\"" +
            labelMarker + "\",\"send_at\":\"not-a-date\"}},\"thresholds\":{");

        string rendered = await RenderedCliRun(record + Environment.NewLine);

        Assert.Contains("Could not parse the 'expected' field", rendered);
        Assert.Contains("JsonException", rendered);
        Assert.DoesNotContain(labelMarker, rendered);
        Assert.DoesNotContain("   at System.Text.Json", rendered);
    }
}
