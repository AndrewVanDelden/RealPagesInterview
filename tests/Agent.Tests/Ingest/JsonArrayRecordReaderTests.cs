using Agent.Common;
using Agent.Domain;
using Agent.Ingest;
using Xunit;

namespace Agent.Tests.Ingest;

public class JsonArrayRecordReaderTests
{
    private static readonly JsonArrayRecordReader<AgentOutput> Reader = new();

    [Fact]
    public async Task ReadAll_RoundTripsWhatTheWriterWrote()
    {
        var sent = new AgentOutput(
            new NextMessage(CommunicationChannel.Email, DateTimeOffset.Parse("2025-12-09T10:00:00-06:00"), "Tour Oak Ridge", "Hi Taylor, reply STOP to opt out.", new Cta("schedule_tour", null, new Uri("https://oakridge.example/tour"))),
            new NextAction("follow_up_in_days", null, 3));
        var suppressed = new AgentOutput(new NextMessage(CommunicationChannel.None), new NextAction("no_op", Reason: "no_contact_consent"));
        var buffer = new StringWriter();
        await new JsonArrayRecordWriter<AgentOutput>().WriteAllAsync(buffer, [sent, suppressed]);

        Result<IReadOnlyList<AgentOutput>> result = Reader.ReadAll(new StringReader(buffer.ToString()));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(sent, result.Value[0]);
        Assert.Equal(suppressed, result.Value[1]);
    }

    [Fact]
    public void ReadAll_EmptyArray_SuccessWithNoRecords()
    {
        Result<IReadOnlyList<AgentOutput>> result = Reader.ReadAll(new StringReader("[]"));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{\"next_action\":{\"type\":\"no_op\"}}")]
    [InlineData("[{\"next_message\":null}]")]
    [InlineData("null")]
    [InlineData("")]
    public void ReadAll_NotAnArrayOfRecords_Failure(string content)
    {
        Result<IReadOnlyList<AgentOutput>> result = Reader.ReadAll(new StringReader(content));

        Assert.False(result.IsSuccess);
        Assert.Contains("JSON array", result.Error);
    }
}
