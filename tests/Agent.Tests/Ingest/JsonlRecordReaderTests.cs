using System.Text.Json;
using Agent.Domain;
using Agent.Ingest;
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

    private static void WithTempFile(string content, Action<string> action)
    {
        string tempFilePath = Path.GetTempFileName();
        File.WriteAllText(tempFilePath, content);

        try
        {
            action(tempFilePath);
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }

    [Fact]
    public void ReadAll_ParsesSampleJsonl_ReturnsExactlyTwoCases()
    {
        IReadOnlyList<ProspectCase> cases = Reader.ReadAll(SampleFilePath);

        Assert.Equal(2, cases.Count);
    }

    [Fact]
    public void ReadAll_ParsesSampleJsonl_PopulatesShortHorizonSmsCase()
    {
        ProspectCase shortHorizonCase = Reader.ReadAll(SampleFilePath)[0];

        Assert.Equal("prospect_welcome_day0", shortHorizonCase.TaskId);
        Assert.Equal("prospect", shortHorizonCase.Persona);
        Assert.True(shortHorizonCase.Consent.SmsOptIn);
        Assert.False(shortHorizonCase.Consent.VoiceOptIn);
        Assert.Equal([CommunicationChannel.Sms, CommunicationChannel.Email], shortHorizonCase.ChannelPreferences);
        Assert.Equal("Oak Ridge Apartments", shortHorizonCase.Input.PropertyName);
        Assert.Equal("Taylor", shortHorizonCase.Input.Profile.FirstName);
        Assert.Equal("Richardson, TX", shortHorizonCase.Input.Profile.CityInterest);
        Assert.Equal("book_tour", shortHorizonCase.Assertions.Constraints.PrimaryCta);
        Assert.Equal(2000, shortHorizonCase.Thresholds.P95LatencyMs);
        Assert.NotNull(shortHorizonCase.Expected);
        Assert.NotNull(shortHorizonCase.Expected!.NextMessage);
        Assert.Equal(CommunicationChannel.Sms, shortHorizonCase.Expected.NextMessage!.Channel);
        Assert.Equal("start_cadence", shortHorizonCase.Expected.NextAction.Type);
    }

    [Fact]
    public void ReadAll_ParsesSampleJsonl_PopulatesLongHorizonEmailCase()
    {
        ProspectCase longHorizonCase = Reader.ReadAll(SampleFilePath)[1];

        Assert.Equal("prospect_long_horizon_day3", longHorizonCase.TaskId);
        Assert.False(longHorizonCase.Consent.SmsOptIn);
        Assert.True(longHorizonCase.Consent.EmailOptIn);
        Assert.Equal([CommunicationChannel.Email, CommunicationChannel.Sms], longHorizonCase.ChannelPreferences);
        Assert.Equal(["pool", "fitness"], longHorizonCase.Input.Profile.AmenityInterest);
        Assert.Null(longHorizonCase.Input.Profile.CityInterest);
        Assert.Equal("follow_up_in_days", longHorizonCase.Expected!.NextAction.Type);
        Assert.Equal(3, longHorizonCase.Expected.NextAction.Value);
    }

    [Fact]
    public void ReadAll_SkipsBlankLines()
    {
        WithTempFile("\n   \n" + File.ReadAllText(SampleFilePath), tempFilePath =>
        {
            IReadOnlyList<ProspectCase> cases = Reader.ReadAll(tempFilePath);

            Assert.Equal(2, cases.Count);
        });
    }

    [Fact]
    public void ReadAll_ThrowsInvalidDataException_WithLineNumber_WhenLineDeserializesToNull()
    {
        WithTempFile("null" + Environment.NewLine, tempFilePath =>
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => Reader.ReadAll(tempFilePath));

            Assert.Contains("Line 1", exception.Message);
        });
    }

    [Fact]
    public void ReadAll_ThrowsInvalidDataException_WithLineNumber_WhenLineIsMalformedJson()
    {
        WithTempFile(MinimalValidLine + Environment.NewLine + "{not valid json" + Environment.NewLine, tempFilePath =>
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => Reader.ReadAll(tempFilePath));

            Assert.Contains("Line 2", exception.Message);
            Assert.IsType<JsonException>(exception.InnerException);
        });
    }

    [Fact]
    public void ReadAll_ThrowsInvalidDataException_WhenRequiredFieldIsNull()
    {
        string lineWithNullRequiredField = MinimalValidLine.Replace("\"task_id\":\"minimal\"", "\"task_id\":null");

        WithTempFile(lineWithNullRequiredField + Environment.NewLine, tempFilePath =>
        {
            Assert.Throws<InvalidDataException>(() => Reader.ReadAll(tempFilePath));
        });
    }

    [Fact]
    public void ReadAll_ParsesSuppressedExpectedOutcome_WhenNextMessageIsNull()
    {
        string suppressedLine = MinimalValidLine.Replace(
            "\"next_message\":{\"channel\":\"sms\",\"body\":\"hi\"}",
            "\"next_message\":null");

        WithTempFile(suppressedLine + Environment.NewLine, tempFilePath =>
        {
            ProspectCase parsedCase = Reader.ReadAll(tempFilePath)[0];

            Assert.NotNull(parsedCase.Expected);
            Assert.Null(parsedCase.Expected!.NextMessage);
            Assert.Equal("start_cadence", parsedCase.Expected.NextAction.Type);
        });
    }
}
