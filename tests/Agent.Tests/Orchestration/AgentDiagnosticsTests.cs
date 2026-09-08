using System.Text.Json;
using Agent.Common;
using Agent.Orchestration;
using Xunit;

namespace Agent.Tests.Orchestration;

public class AgentDiagnosticsTests
{
    // D3: the suppression reason is spelled in snake_case on the wire, the same spelling
    // next_action.reason uses, so a diagnostics row and an output row read alike.
    [Fact]
    public void Serializes_SuppressionReason_InSnakeCase()
    {
        var diagnostics = new AgentDiagnostics(true, null, false, 0, SuppressionReason.NoContactConsent);

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"suppression_reason\":\"no_contact_consent\"", json);
    }

    [Fact]
    public void Serializes_DefaultSuppressionReason_AsNone()
    {
        var diagnostics = new AgentDiagnostics(true, true, true, 0);

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"suppression_reason\":\"none\"", json);
    }
}
