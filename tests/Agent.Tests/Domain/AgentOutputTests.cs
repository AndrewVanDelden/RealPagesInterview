using System.Text.Json;
using Agent.Common;
using Agent.Domain;
using Xunit;

namespace Agent.Tests.Domain;

public class AgentOutputTests
{
    [Fact]
    public void RoundTrips_ThroughAgentJsonOptions_WithNextMessagePresent()
    {
        var output = new AgentOutput(
            new NextMessage(CommunicationChannel.Sms, null, null, "hi", null),
            new NextAction("start_cadence", null, null));

        string json = JsonSerializer.Serialize(output, AgentJsonOptions.Default);
        AgentOutput? roundTripped = JsonSerializer.Deserialize<AgentOutput>(json, AgentJsonOptions.Default);

        Assert.Contains("\"channel\":\"sms\"", json);
        Assert.Contains("\"next_action\"", json);
        Assert.NotNull(roundTripped);
        Assert.NotNull(roundTripped!.NextMessage);
        Assert.Equal(CommunicationChannel.Sms, roundTripped.NextMessage!.Channel);
        Assert.Equal("start_cadence", roundTripped.NextAction.Type);
    }

    [Fact]
    public void RoundTrips_ThroughAgentJsonOptions_WhenNextMessageIsSuppressed()
    {
        var output = new AgentOutput(null, new NextAction("suppress", "no_consented_channel", null));

        string json = JsonSerializer.Serialize(output, AgentJsonOptions.Default);
        AgentOutput? roundTripped = JsonSerializer.Deserialize<AgentOutput>(json, AgentJsonOptions.Default);

        Assert.Contains("\"next_message\":null", json);
        Assert.NotNull(roundTripped);
        Assert.Null(roundTripped!.NextMessage);
        Assert.Equal("suppress", roundTripped.NextAction.Type);
        Assert.Equal("no_consented_channel", roundTripped.NextAction.Name);
    }
}
