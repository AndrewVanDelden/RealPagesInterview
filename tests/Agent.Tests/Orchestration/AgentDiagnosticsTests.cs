using System.Text.Json;
using Agent.Common;
using Agent.Decisions;
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

    // D18 and the Phase 3 check: the diagnostics say which horizon branch the record took,
    // how many days that was, and which catalog row answered. Both enums are spelled the
    // way every other enum on the wire is (D3).
    [Fact]
    public void Serializes_ActionPlan_WithSnakeCaseBranchAndSource()
    {
        var diagnostics = new AgentDiagnostics(
            true,
            true,
            true,
            0,
            SuppressionReason.None,
            new ActionPlanNotes(HorizonBranch.Long, 68, ActionSource.GenericRowNoBranch));

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"action_plan\":{\"branch\":\"long\",\"horizon_days\":68,\"source\":\"generic_row_no_branch\"}", json);
    }

    // The planner never ran on a record the consent gate suppressed, so there is no plan to
    // explain and the diagnostics say nothing rather than reporting a branch nobody took.
    [Fact]
    public void Serializes_AbsentActionPlan_AsNull()
    {
        var diagnostics = new AgentDiagnostics(true, null, false, 0, SuppressionReason.NoContactConsent);

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"action_plan\":null", json);
    }
}
