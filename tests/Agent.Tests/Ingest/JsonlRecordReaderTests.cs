using System.Text.Json;
using Agent.Common;
using Agent.Domain;
using Agent.Ingest;
using Agent.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Agent.Tests.Ingest;

public class JsonlRecordReaderTests
{
    private static readonly string SampleFilePath = Path.Combine(AppContext.BaseDirectory, "TestData", "sample.jsonl");
    private static readonly IRecordReader Reader = new JsonlRecordReader();

    private const string MinimalValidLine =
        "{\"task_id\":\"minimal\",\"persona\":\"prospect\",\"lifecycle_stage\":\"new\"," +
        "\"consent\":{\"email_opt_in\":true,\"sms_opt_in\":true,\"voice_opt_in\":false}," +
        "\"channel_preferences\":[\"sms\"]," +
        "\"input\":{\"property_name\":\"Oak Ridge\",\"move_date_target\":\"2026-01-10\",\"last_interaction\":\"2025-12-08T15:04:00Z\",\"timezone\":\"America/Chicago\",\"language\":\"en\",\"profile\":{\"first_name\":\"Taylor\"}}," +
        "\"assertions\":{\"required_states\":[],\"constraints\":{\"no_pii_leak\":true,\"include_opt_out_instructions\":true,\"primary_cta\":\"book_tour\"}}," +
        "\"thresholds\":{\"p95_latency_ms\":2000,\"personalization_score_min\":0.85,\"reply_classification_f1_min\":0.9,\"safety_violations_max\":0}," +
        "\"expected\":{\"next_message\":{\"channel\":\"sms\",\"body\":\"hi\"},\"next_action\":{\"type\":\"start_cadence\"}}}";

    private static IReadOnlyList<ProspectCase> ReadSample()
    {
        using TextReader reader = new StreamReader(SampleFilePath);
        return Reader.ReadAll(reader);
    }

    private static readonly IReadOnlyList<ProspectCase> SampleCases = ReadSample();

    [Fact]
    public void ReadAll_ParsesSampleJsonl_ReturnsExactlyTwoCases()
    {
        Assert.Equal(2, SampleCases.Count);
    }

    [Fact]
    public void ReadAll_ParsesSampleJsonl_PopulatesShortHorizonSmsCase()
    {
        ProspectCase shortHorizonCase = SampleCases[0];

        Assert.Equal("prospect_welcome_day0", shortHorizonCase.TaskId);
        Assert.Equal("prospect", shortHorizonCase.Persona);
        Assert.True(shortHorizonCase.Consent.SmsOptIn);
        Assert.False(shortHorizonCase.Consent.VoiceOptIn);
        Assert.Equal([CommunicationChannel.Sms, CommunicationChannel.Email], shortHorizonCase.ChannelPreferences);
        Assert.Equal("Oak Ridge Apartments", shortHorizonCase.Input.PropertyName);
        Assert.Equal("America/Chicago", shortHorizonCase.Input.TimeZoneId);
        Assert.Equal(new DateOnly(2026, 1, 10), shortHorizonCase.Input.MoveDateTarget);
        Assert.Equal(DateTimeOffset.Parse("2025-12-08T15:04:00Z"), shortHorizonCase.Input.LastInteraction);
        Assert.Equal("Taylor", shortHorizonCase.Input.Profile.FirstName);
        Assert.Equal("Richardson, TX", shortHorizonCase.Input.Profile.CityInterest);
        Assert.Equal("book_tour", shortHorizonCase.Assertions.Constraints.PrimaryCta);
        Assert.Equal(2000, shortHorizonCase.Thresholds.P95LatencyMs);
        Assert.Equal(0.9, shortHorizonCase.Thresholds.ReplyClassificationF1Min);
        Assert.Equal(0, shortHorizonCase.Thresholds.SafetyViolationsMax);
        Assert.NotNull(shortHorizonCase.Expected);
        Assert.NotNull(shortHorizonCase.Expected!.NextMessage);
        Assert.Equal(CommunicationChannel.Sms, shortHorizonCase.Expected.NextMessage!.Channel);
        Assert.Contains("book a time on Thursday or Friday", shortHorizonCase.Expected.NextMessage.Body);
        Assert.Equal(["Thu", "Fri"], shortHorizonCase.Expected.NextMessage.Cta!.Options);
        Assert.Null(shortHorizonCase.Expected.NextMessage.Cta.Link);
        Assert.Equal("start_cadence", shortHorizonCase.Expected.NextAction.Type);
    }

    [Fact]
    public void ReadAll_ParsesSampleJsonl_PopulatesLongHorizonEmailCase()
    {
        ProspectCase longHorizonCase = SampleCases[1];

        Assert.Equal("prospect_long_horizon_day3", longHorizonCase.TaskId);
        Assert.False(longHorizonCase.Consent.SmsOptIn);
        Assert.True(longHorizonCase.Consent.EmailOptIn);
        Assert.Equal([CommunicationChannel.Email, CommunicationChannel.Sms], longHorizonCase.ChannelPreferences);
        Assert.Equal(["pool", "fitness"], longHorizonCase.Input.Profile.AmenityInterest);
        Assert.Null(longHorizonCase.Input.Profile.CityInterest);
        Assert.Null(longHorizonCase.Assertions.Constraints.NoSensitiveDiscrimination);
        Assert.NotNull(longHorizonCase.Expected!.NextMessage);
        Assert.Contains("See the pool & fitness rooms", longHorizonCase.Expected.NextMessage!.Subject);
        Assert.Equal(new Uri("https://oakridge.example/tour"), longHorizonCase.Expected.NextMessage.Cta!.Link);
        Assert.Null(longHorizonCase.Expected.NextMessage.Cta.Options);
        Assert.Equal("follow_up_in_days", longHorizonCase.Expected.NextAction.Type);
        Assert.Equal(3, longHorizonCase.Expected.NextAction.Value);
    }

    [Fact]
    public void ReadAll_SkipsBlankLines()
    {
        using TextReader reader = new StringReader("\n   \n" + File.ReadAllText(SampleFilePath));

        IReadOnlyList<ProspectCase> cases = Reader.ReadAll(reader);

        Assert.Equal(2, cases.Count);
    }

    [Fact]
    public void ReadAll_ThrowsInvalidDataException_WithLineNumber_WhenLineDeserializesToNull()
    {
        using TextReader reader = new StringReader("null" + Environment.NewLine);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => Reader.ReadAll(reader));

        Assert.Contains("Line 1", exception.Message);
    }

    [Fact]
    public void ReadAll_ThrowsInvalidDataException_WithLineNumber_WhenLineIsMalformedJson()
    {
        using TextReader reader = new StringReader(MinimalValidLine + Environment.NewLine + "{not valid json" + Environment.NewLine);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => Reader.ReadAll(reader));

        Assert.Contains("Line 2", exception.Message);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    [Fact]
    public void ReadAll_ThrowsInvalidDataException_WithLineNumber_WhenLineIsValidJsonButNotAnObject()
    {
        using TextReader reader = new StringReader(MinimalValidLine + Environment.NewLine + "[1,2,3]" + Environment.NewLine);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => Reader.ReadAll(reader));

        Assert.Contains("Line 2", exception.Message);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    [Fact]
    public void ReadAll_ThrowsInvalidDataException_WhenRequiredFieldIsNull()
    {
        string lineWithNullRequiredField = MinimalValidLine.Replace("\"task_id\":\"minimal\"", "\"task_id\":null");
        using TextReader reader = new StringReader(lineWithNullRequiredField + Environment.NewLine);

        Assert.Throws<InvalidDataException>(() => Reader.ReadAll(reader));
    }

    [Fact]
    public void ReadAll_ParsesSuppressedExpectedOutcome_WhenNextMessageIsNull()
    {
        string suppressedLine = MinimalValidLine.Replace(
            "\"next_message\":{\"channel\":\"sms\",\"body\":\"hi\"}",
            "\"next_message\":null");
        using TextReader reader = new StringReader(suppressedLine + Environment.NewLine);

        ProspectCase parsedCase = Reader.ReadAll(reader)[0];

        Assert.NotNull(parsedCase.Expected);
        Assert.Null(parsedCase.Expected!.NextMessage);
        Assert.Equal("start_cadence", parsedCase.Expected.NextAction.Type);
    }

    // "expected" is the scoring oracle, not something the agent reads to make its own
    // decisions (DESIGN.md section 2). A hold-out file's expected shape is not under
    // our control, so a value outside our schema (an unrecognized channel, a novel
    // next_action shape) must not take down the whole record - only the fields the
    // agent actually depends on (consent, channel_preferences, input, assertions,
    // thresholds) are required to be strict.
    [Fact]
    public void ReadAll_ExpectedHasUnrecognizedChannelValue_ParsesRecordWithNullExpectedInsteadOfThrowing()
    {
        string lineWithUnknownChannel = MinimalValidLine.Replace(
            "\"expected\":{\"next_message\":{\"channel\":\"sms\",\"body\":\"hi\"},\"next_action\":{\"type\":\"start_cadence\"}}",
            "\"expected\":{\"next_message\":{\"channel\":\"none\",\"body\":\"hi\"},\"next_action\":{\"type\":\"no_op\"}}");
        using TextReader reader = new StringReader(lineWithUnknownChannel + Environment.NewLine);

        ProspectCase parsedCase = Reader.ReadAll(reader)[0];

        Assert.Equal("minimal", parsedCase.TaskId);
        Assert.Equal("book_tour", parsedCase.Assertions.Constraints.PrimaryCta);
        Assert.Null(parsedCase.Expected);
    }

    [Fact]
    public void ReadAll_ExpectedHasNullRequiredNextActionType_ParsesRecordWithNullExpectedInsteadOfThrowing()
    {
        string lineWithBadExpected = MinimalValidLine.Replace(
            "\"next_action\":{\"type\":\"start_cadence\"}",
            "\"next_action\":{\"type\":null}");
        using TextReader reader = new StringReader(lineWithBadExpected + Environment.NewLine);

        ProspectCase parsedCase = Reader.ReadAll(reader)[0];

        Assert.Equal("minimal", parsedCase.TaskId);
        Assert.Null(parsedCase.Expected);
    }

    [Fact]
    public void ReadAll_ExpectedPropertyMissingEntirely_ParsesRecordWithNullExpected()
    {
        string lineWithoutExpected = MinimalValidLine.Replace(
            ",\"expected\":{\"next_message\":{\"channel\":\"sms\",\"body\":\"hi\"},\"next_action\":{\"type\":\"start_cadence\"}}",
            string.Empty);
        using TextReader reader = new StringReader(lineWithoutExpected + Environment.NewLine);

        ProspectCase parsedCase = Reader.ReadAll(reader)[0];

        Assert.Null(parsedCase.Expected);
    }

    // CaseConstraints.PrimaryCta is nullable (real hold-out data can omit primary_cta
    // entirely - see TalkingPoints.md Sprint 7). An explicit JSON null must parse the same
    // way as a missing key, not throw: RespectNullableAnnotations only rejects an explicit
    // null against a *non-nullable* property, so this is only safe because the property is
    // honestly typed string?, not because of any special-case handling here.
    [Fact]
    public void ReadAll_ExplicitNullPrimaryCta_ParsesToNullInsteadOfThrowing()
    {
        string lineWithExplicitNullCta = MinimalValidLine.Replace("\"primary_cta\":\"book_tour\"", "\"primary_cta\":null");
        using TextReader reader = new StringReader(lineWithExplicitNullCta + Environment.NewLine);

        ProspectCase parsedCase = Reader.ReadAll(reader)[0];

        Assert.Null(parsedCase.Assertions.Constraints.PrimaryCta);
    }

    // LenientExpectedOutcomeConverter has no constructor-injection path (JsonConverter<T>
    // instances are stateless and shared - see AgentLog's own remarks), so this is the one
    // place in the codebase that reaches a logger through AgentLog's AsyncLocal accessor
    // rather than a constructor parameter. Proves the wiring actually fires, not just that
    // parsing doesn't throw (already covered by the sibling tests above).
    [Fact]
    public void ReadAll_ExpectedFailsToParse_LogsWarningThroughAgentLog()
    {
        var capturingLogger = new CapturingLogger<JsonlRecordReaderTests>();
        string lineWithUnknownChannel = MinimalValidLine.Replace(
            "\"expected\":{\"next_message\":{\"channel\":\"sms\",\"body\":\"hi\"},\"next_action\":{\"type\":\"start_cadence\"}}",
            "\"expected\":{\"next_message\":{\"channel\":\"none\",\"body\":\"hi\"},\"next_action\":{\"type\":\"no_op\"}}");
        using TextReader reader = new StringReader(lineWithUnknownChannel + Environment.NewLine);

        using (AgentLog.Configure(new FakeLoggerFactory(capturingLogger)))
        {
            Reader.ReadAll(reader);
        }

        Assert.Contains(capturingLogger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void ProspectCase_WithExpectedPresent_RoundTripsThroughSerializeAndDeserialize()
    {
        ProspectCase original = Reader.ReadAll(new StringReader(MinimalValidLine))[0];

        string serialized = JsonSerializer.Serialize(original, Agent.Common.AgentJsonOptions.Default);
        ProspectCase roundTripped = Reader.ReadAll(new StringReader(serialized))[0];

        Assert.NotNull(roundTripped.Expected);
        Assert.Equal("start_cadence", roundTripped.Expected!.NextAction.Type);
    }
}
