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
    private static readonly string HoldoutFilePath = Path.Combine(AppContext.BaseDirectory, "TestData", "holdout_12.jsonl");
    private static readonly JsonlRecordReader Reader = new();

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
        return Reader.ReadAll(reader).Select(result => result.Value).ToList();
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
        Assert.Equal("Oak Ridge Apartments", shortHorizonCase.Input!.PropertyName);
        Assert.Equal("America/Chicago", shortHorizonCase.Input!.TimeZoneId);
        Assert.Equal(new DateOnly(2026, 1, 10), shortHorizonCase.Input!.MoveDateTarget);
        Assert.Equal(DateTimeOffset.Parse("2025-12-08T15:04:00Z"), shortHorizonCase.Input!.LastInteraction);
        Assert.Equal("Taylor", shortHorizonCase.Input!.Profile!.FirstName);
        Assert.Equal("Richardson, TX", shortHorizonCase.Input!.Profile!.CityInterest);
        Assert.Equal("book_tour", shortHorizonCase.Assertions!.Constraints!.PrimaryCta);
        Assert.Equal(2000, shortHorizonCase.Thresholds!.P95LatencyMs);
        Assert.Equal(0.9, shortHorizonCase.Thresholds!.ReplyClassificationF1Min);
        Assert.Equal(0, shortHorizonCase.Thresholds!.SafetyViolationsMax);
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
        Assert.Equal(["pool", "fitness"], longHorizonCase.Input!.Profile!.AmenityInterest);
        Assert.Null(longHorizonCase.Input!.Profile!.CityInterest);
        Assert.Null(longHorizonCase.Assertions!.Constraints!.NoSensitiveDiscrimination);
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

        IReadOnlyList<Result<ProspectCase>> results = Reader.ReadAll(reader);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.True(result.IsSuccess));
    }

    [Fact]
    public void ReadAll_ReturnsFailureWithLineNumber_WhenLineDeserializesToNull()
    {
        using TextReader reader = new StringReader("null" + Environment.NewLine);

        Result<ProspectCase> result = Assert.Single(Reader.ReadAll(reader));

        Assert.False(result.IsSuccess);
        Assert.Contains("Line 1", result.Error);
    }

    [Fact]
    public void ReadAll_ReturnsFailureWithLineNumber_WhenLineIsMalformedJson()
    {
        using TextReader reader = new StringReader(MinimalValidLine + Environment.NewLine + "{not valid json" + Environment.NewLine);

        IReadOnlyList<Result<ProspectCase>> results = Reader.ReadAll(reader);

        Assert.Equal(2, results.Count);
        Assert.True(results[0].IsSuccess);
        Assert.False(results[1].IsSuccess);
        Assert.Contains("Line 2 failed to parse", results[1].Error);
    }

    [Fact]
    public void ReadAll_ReturnsFailureWithLineNumber_WhenLineIsValidJsonButNotAnObject()
    {
        using TextReader reader = new StringReader(MinimalValidLine + Environment.NewLine + "[1,2,3]" + Environment.NewLine);

        IReadOnlyList<Result<ProspectCase>> results = Reader.ReadAll(reader);

        Assert.Equal(2, results.Count);
        Assert.False(results[1].IsSuccess);
        Assert.Contains("Line 2 failed to parse", results[1].Error);
    }

    [Fact]
    public void ReadAll_ReturnsFailure_WhenRequiredFieldIsNull()
    {
        string lineWithNullRequiredField = MinimalValidLine.Replace("\"task_id\":\"minimal\"", "\"task_id\":null");
        using TextReader reader = new StringReader(lineWithNullRequiredField + Environment.NewLine);

        Result<ProspectCase> result = Assert.Single(Reader.ReadAll(reader));

        Assert.False(result.IsSuccess);
        Assert.Contains("Line 1", result.Error);
    }

    // D1 (DECISION_LOG.md): every member is optional except task_id, consent, and
    // channel_preferences. An absent optional value type is null, never a silent default
    // (the year-0001 dates of retrospective finding 4), and never an error row.
    [Fact]
    public void ReadAll_AbsentOptionalValueTypeProperty_ParsesAsNull()
    {
        string lineWithoutMoveDate = MinimalValidLine.Replace("\"move_date_target\":\"2026-01-10\",", string.Empty);
        using TextReader reader = new StringReader(lineWithoutMoveDate + Environment.NewLine);

        ProspectCase parsedCase = Assert.Single(Reader.ReadAll(reader)).Value;

        Assert.NotNull(parsedCase.Input);
        Assert.Null(parsedCase.Input!.MoveDateTarget);
        Assert.Equal(DateTimeOffset.Parse("2025-12-08T15:04:00Z"), parsedCase.Input.LastInteraction);
    }

    [Fact]
    public void ReadAll_OnlyTheThreeRequiredMembers_ParsesWithEverythingElseNull()
    {
        const string requiredOnly = "{\"task_id\":\"bare\",\"consent\":{\"sms_opt_in\":true},\"channel_preferences\":[\"sms\"]}";
        using TextReader reader = new StringReader(requiredOnly + Environment.NewLine);

        ProspectCase parsedCase = Assert.Single(Reader.ReadAll(reader)).Value;

        Assert.Equal("bare", parsedCase.TaskId);
        Assert.Null(parsedCase.Persona);
        Assert.Null(parsedCase.LifecycleStage);
        Assert.Null(parsedCase.Input);
        Assert.Null(parsedCase.Assertions);
        Assert.Null(parsedCase.Thresholds);
        Assert.Null(parsedCase.Expected);
        Assert.True(parsedCase.Consent.SmsOptIn);
        Assert.Null(parsedCase.Consent.EmailOptIn);
    }

    [Fact]
    public void ReadAll_ReturnsFailureWithLineNumber_WhenChannelPreferencesIsAbsent()
    {
        string lineWithoutPreferences = MinimalValidLine.Replace("\"channel_preferences\":[\"sms\"],", string.Empty);
        using TextReader reader = new StringReader(lineWithoutPreferences + Environment.NewLine);

        Result<ProspectCase> result = Assert.Single(Reader.ReadAll(reader));

        Assert.False(result.IsSuccess);
        Assert.Contains("Line 1", result.Error);
        Assert.Contains("channel_preferences", result.Error);
    }

    // A3: an unrecognized channel name is a real value the selector skips, never a reason
    // to fail the record.
    [Fact]
    public void ReadAll_UnrecognizedChannelName_ParsesAsUnknownChannel()
    {
        string lineWithOddChannel = MinimalValidLine.Replace("\"channel_preferences\":[\"sms\"]", "\"channel_preferences\":[\"carrier_pigeon\",\"sms\"]");
        using TextReader reader = new StringReader(lineWithOddChannel + Environment.NewLine);

        ProspectCase parsedCase = Assert.Single(Reader.ReadAll(reader)).Value;

        Assert.Equal([CommunicationChannel.Unknown, CommunicationChannel.Sms], parsedCase.ChannelPreferences);
    }

    [Fact]
    public void ReadAll_ChannelPreferenceEntriesNotChannelNames_ParseAsUnknown()
    {
        string lineWithOddEntries = MinimalValidLine.Replace("\"channel_preferences\":[\"sms\"]", "\"channel_preferences\":[\"99\",7,\"sms\"]");
        using TextReader reader = new StringReader(lineWithOddEntries + Environment.NewLine);

        ProspectCase parsedCase = Assert.Single(Reader.ReadAll(reader)).Value;

        Assert.Equal([CommunicationChannel.Unknown, CommunicationChannel.Unknown, CommunicationChannel.Sms], parsedCase.ChannelPreferences);
    }

    // Enum.TryParse also accepts a purely numeric string and casts it to the underlying
    // ordinal (Sms = 1) - a numeric channel name is never valid input and must not be
    // mistaken for the real channel that happens to share its ordinal.
    [Fact]
    public void ReadAll_ChannelPreferenceNumericStringMatchingARealOrdinal_ParsesAsUnknown()
    {
        string lineWithNumericEntry = MinimalValidLine.Replace("\"channel_preferences\":[\"sms\"]", "\"channel_preferences\":[\"1\",\"sms\"]");
        using TextReader reader = new StringReader(lineWithNumericEntry + Environment.NewLine);

        ProspectCase parsedCase = Assert.Single(Reader.ReadAll(reader)).Value;

        Assert.Equal([CommunicationChannel.Unknown, CommunicationChannel.Sms], parsedCase.ChannelPreferences);
    }

    // A16: members the record types do not declare are kept, at any depth, so diagnostics
    // can name them and nothing the file carries is silently dropped.
    [Fact]
    public void ReadAll_UnknownMembersAtAnyDepth_AreRetainedOnTheRecord()
    {
        string lineWithExtras = MinimalValidLine
            .Replace("\"persona\":\"prospect\"", "\"persona\":\"prospect\",\"campaign\":\"spring\"")
            .Replace("\"language\":\"en\"", "\"language\":\"en\",\"unit\":\"A-204\"")
            .Replace("\"first_name\":\"Taylor\"", "\"first_name\":\"Taylor\",\"budget_max\":1700")
            .Replace("\"primary_cta\":\"book_tour\"", "\"primary_cta\":\"book_tour\",\"respect_consent\":true")
            .Replace("\"safety_violations_max\":0", "\"safety_violations_max\":0,\"locale_accuracy_min\":0.95");
        using TextReader reader = new StringReader(lineWithExtras + Environment.NewLine);

        ProspectCase parsedCase = Assert.Single(Reader.ReadAll(reader)).Value;

        Assert.Equal("spring", parsedCase.UnknownMembers!["campaign"].GetString());
        Assert.Equal("A-204", parsedCase.Input!.UnknownMembers!["unit"].GetString());
        Assert.Equal(1700, parsedCase.Input.Profile!.UnknownMembers!["budget_max"].GetInt32());
        Assert.True(parsedCase.Assertions!.Constraints!.UnknownMembers!["respect_consent"].GetBoolean());
        Assert.Equal(0.95, parsedCase.Thresholds!.UnknownMembers!["locale_accuracy_min"].GetDouble());
    }

    // D11: the twelve-record evaluation set is in the repo. Every line must be a success
    // row before anything can be scored; this is the Phase 2 precondition for the harness.
    [Fact]
    public void ReadAll_ParsesHoldoutTwelve_EveryLineIsASuccessRow()
    {
        using TextReader reader = new StreamReader(HoldoutFilePath);

        IReadOnlyList<Result<ProspectCase>> results = Reader.ReadAll(reader);

        Assert.Equal(12, results.Count);
        Assert.All(results, result => Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Error));
    }

    [Fact]
    public void ReadAll_ReturnsFailureWithLineNumber_WhenRequiredObjectPropertyIsAbsent()
    {
        string lineWithoutConsent = MinimalValidLine.Replace(
            "\"consent\":{\"email_opt_in\":true,\"sms_opt_in\":true,\"voice_opt_in\":false},",
            string.Empty);
        using TextReader reader = new StringReader(lineWithoutConsent + Environment.NewLine);

        Result<ProspectCase> result = Assert.Single(Reader.ReadAll(reader));

        Assert.False(result.IsSuccess);
        Assert.Contains("Line 1", result.Error);
        Assert.Contains("consent", result.Error);
    }

    // Blank lines are skipped but still counted, or the reported line number stops
    // matching what an editor shows for the same file.
    [Fact]
    public void ReadAll_BlankLinesBeforeFailingLine_CountTowardTheLineNumber()
    {
        using TextReader reader = new StringReader("\n   \n{not valid json" + Environment.NewLine);

        Result<ProspectCase> result = Assert.Single(Reader.ReadAll(reader));

        Assert.False(result.IsSuccess);
        Assert.Contains("Line 3", result.Error);
    }

    // One bad line is one failure row (HB: report failures per record, never per batch);
    // every other line still comes back parsed.
    [Fact]
    public void ReadAll_OneBadLineAmongGoodOnes_ReturnsEveryOtherRecord()
    {
        string secondValidLine = MinimalValidLine.Replace("\"task_id\":\"minimal\"", "\"task_id\":\"second\"");
        using TextReader reader = new StringReader(string.Join(Environment.NewLine, MinimalValidLine, "{not valid json", secondValidLine));

        IReadOnlyList<Result<ProspectCase>> results = Reader.ReadAll(reader);

        Assert.Equal(3, results.Count);
        Assert.Equal("minimal", results[0].Value.TaskId);
        Assert.False(results[1].IsSuccess);
        Assert.Equal("second", results[2].Value.TaskId);
    }

    [Fact]
    public void ReadAll_ParsesSuppressedExpectedOutcome_WhenNextMessageIsNull()
    {
        string suppressedLine = MinimalValidLine.Replace(
            "\"next_message\":{\"channel\":\"sms\",\"body\":\"hi\"}",
            "\"next_message\":null");
        using TextReader reader = new StringReader(suppressedLine + Environment.NewLine);

        ProspectCase parsedCase = Reader.ReadAll(reader)[0].Value;

        Assert.NotNull(parsedCase.Expected);
        Assert.Null(parsedCase.Expected!.NextMessage);
        Assert.Equal("start_cadence", parsedCase.Expected.NextAction.Type);
    }

    // "expected" is the scoring oracle, not something the agent reads to make its own
    // decisions (DESIGN.md section 2). An unrecognized channel name in it is the Unknown
    // value, so the record stays scoreable and the channel check simply does not match.
    [Fact]
    public void ReadAll_ExpectedHasUnrecognizedChannelValue_ParsesToUnknownChannel()
    {
        string lineWithUnknownChannel = MinimalValidLine.Replace(
            "\"expected\":{\"next_message\":{\"channel\":\"sms\",\"body\":\"hi\"},\"next_action\":{\"type\":\"start_cadence\"}}",
            "\"expected\":{\"next_message\":{\"channel\":\"carrier_pigeon\",\"body\":\"hi\"},\"next_action\":{\"type\":\"no_op\"}}");
        using TextReader reader = new StringReader(lineWithUnknownChannel + Environment.NewLine);

        ProspectCase parsedCase = Reader.ReadAll(reader)[0].Value;

        Assert.Equal("minimal", parsedCase.TaskId);
        Assert.Equal(CommunicationChannel.Unknown, parsedCase.Expected!.NextMessage!.Channel);
        Assert.Equal("no_op", parsedCase.Expected.NextAction.Type);
    }

    [Fact]
    public void ReadAll_ExpectedHasNullRequiredNextActionType_ParsesRecordWithNullExpectedInsteadOfThrowing()
    {
        string lineWithBadExpected = MinimalValidLine.Replace(
            "\"next_action\":{\"type\":\"start_cadence\"}",
            "\"next_action\":{\"type\":null}");
        using TextReader reader = new StringReader(lineWithBadExpected + Environment.NewLine);

        ProspectCase parsedCase = Reader.ReadAll(reader)[0].Value;

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

        ProspectCase parsedCase = Reader.ReadAll(reader)[0].Value;

        Assert.Null(parsedCase.Expected);
    }

    // The hold-out oracle spells a suppressed message as a next_message object with
    // channel "none" and null fields (retrospective D3), not as a null object. Both
    // spellings must parse so the record can be scored.
    [Fact]
    public void ReadAll_ExpectedSuppressedWithChannelNone_ParsesToNoneChannelAndNullBody()
    {
        string suppressedLine = MinimalValidLine.Replace(
            "\"next_message\":{\"channel\":\"sms\",\"body\":\"hi\"}",
            "\"next_message\":{\"channel\":\"none\",\"send_at\":null,\"subject\":null,\"body\":null,\"cta\":null}");
        using TextReader reader = new StringReader(suppressedLine + Environment.NewLine);

        ProspectCase parsedCase = Reader.ReadAll(reader)[0].Value;

        Assert.NotNull(parsedCase.Expected);
        Assert.NotNull(parsedCase.Expected!.NextMessage);
        Assert.Equal(CommunicationChannel.None, parsedCase.Expected.NextMessage!.Channel);
        Assert.Null(parsedCase.Expected.NextMessage.Body);
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

        ProspectCase parsedCase = Reader.ReadAll(reader)[0].Value;

        Assert.Null(parsedCase.Assertions!.Constraints!.PrimaryCta);
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
        string lineWithBadExpected = MinimalValidLine.Replace(
            "\"expected\":{\"next_message\":{\"channel\":\"sms\",\"body\":\"hi\"},\"next_action\":{\"type\":\"start_cadence\"}}",
            "\"expected\":\"not an object\"");
        using TextReader reader = new StringReader(lineWithBadExpected + Environment.NewLine);

        using (AgentLog.Configure(new FakeLoggerFactory(capturingLogger)))
        {
            Reader.ReadAll(reader);
        }

        Assert.Contains(capturingLogger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void ProspectCase_WithExpectedPresent_RoundTripsThroughSerializeAndDeserialize()
    {
        ProspectCase original = Reader.ReadAll(new StringReader(MinimalValidLine))[0].Value;

        string serialized = JsonSerializer.Serialize(original, Agent.Common.AgentJsonOptions.Default);
        ProspectCase roundTripped = Reader.ReadAll(new StringReader(serialized))[0].Value;

        Assert.NotNull(roundTripped.Expected);
        Assert.Equal("start_cadence", roundTripped.Expected!.NextAction.Type);
    }
}
