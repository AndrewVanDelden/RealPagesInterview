using Agent.Domain;
using Agent.Ingest;
using Xunit;

namespace Agent.Tests.Ingest;

public class JsonlRecordReaderTests
{
    private static readonly string SampleFilePath = Path.Combine(AppContext.BaseDirectory, "TestData", "sample.jsonl");

    [Fact]
    public void ReadAll_ParsesSampleJsonl_ReturnsExactlyTwoCases()
    {
        IRecordReader reader = new JsonlRecordReader();

        IReadOnlyList<ProspectCase> cases = reader.ReadAll(SampleFilePath);

        Assert.Equal(2, cases.Count);
    }

    [Fact]
    public void ReadAll_ParsesSampleJsonl_PopulatesShortHorizonSmsCase()
    {
        IRecordReader reader = new JsonlRecordReader();

        ProspectCase shortHorizonCase = reader.ReadAll(SampleFilePath)[0];

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
        Assert.Equal(CommunicationChannel.Sms, shortHorizonCase.Expected!.NextMessage.Channel);
        Assert.Equal("start_cadence", shortHorizonCase.Expected.NextAction.Type);
    }

    [Fact]
    public void ReadAll_ParsesSampleJsonl_PopulatesLongHorizonEmailCase()
    {
        IRecordReader reader = new JsonlRecordReader();

        ProspectCase longHorizonCase = reader.ReadAll(SampleFilePath)[1];

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
        string tempFilePath = Path.GetTempFileName();
        File.WriteAllText(tempFilePath, "\n   \n" + File.ReadAllText(SampleFilePath));

        try
        {
            IRecordReader reader = new JsonlRecordReader();

            IReadOnlyList<ProspectCase> cases = reader.ReadAll(tempFilePath);

            Assert.Equal(2, cases.Count);
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }

    [Fact]
    public void ReadAll_ThrowsInvalidDataException_WhenLineDeserializesToNull()
    {
        string tempFilePath = Path.GetTempFileName();
        File.WriteAllText(tempFilePath, "null" + Environment.NewLine);

        try
        {
            IRecordReader reader = new JsonlRecordReader();

            Assert.Throws<InvalidDataException>(() => reader.ReadAll(tempFilePath));
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }
}
