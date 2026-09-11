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

    // Suppression on the wire is a next_message object with channel none and null
    // fields, matching the oracle's shape; next_action omits the members it does not use.
    [Fact]
    public void Serializes_SuppressedOutput_AsNoneObjectWithNullFieldsAndNoOpWithReason()
    {
        var output = new AgentOutput(
            new NextMessage(CommunicationChannel.None),
            new NextAction("no_op", Reason: "no_contact_consent"));

        string json = JsonSerializer.Serialize(output, AgentJsonOptions.Default);

        Assert.Contains("\"channel\":\"none\"", json);
        Assert.Contains("\"send_at\":null", json);
        Assert.Contains("\"body\":null", json);
        Assert.Contains("\"cta\":null", json);
        Assert.Contains("\"type\":\"no_op\"", json);
        Assert.Contains("\"reason\":\"no_contact_consent\"", json);
        Assert.DoesNotContain("\"name\":null", json);
        Assert.DoesNotContain("\"value\":null", json);
    }

    [Fact]
    public void Serializes_NextActionWithValue_OmitsNameAndReason()
    {
        var output = new AgentOutput(new NextMessage(CommunicationChannel.Sms, Body: "hi"), new NextAction("follow_up_in_days", Value: 3));

        string json = JsonSerializer.Serialize(output, AgentJsonOptions.Default);

        Assert.Contains("\"value\":3", json);
        Assert.DoesNotContain("\"name\"", json);
        Assert.DoesNotContain("\"reason\"", json);
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
