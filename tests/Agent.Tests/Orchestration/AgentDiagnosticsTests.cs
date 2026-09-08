using System.Text.Json;
using Agent.Common;
using Agent.Composition;
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

    // D22 and the Phase 3 check: send_at has three inputs (A4's floor, A6's zone, A5's hour),
    // and the diagnostics name all three. The two enums are spelled the way every other enum
    // on the wire is (D3).
    [Fact]
    public void Serializes_Schedule_WithSnakeCaseFloorAndSlot()
    {
        var diagnostics = new AgentDiagnostics(
            true,
            true,
            true,
            0,
            SuppressionReason.None,
            new ActionPlanNotes(HorizonBranch.Short, 32, ActionSource.CatalogRow),
            new ScheduleNotes(ScheduleFloor.LastInteraction, "America/Chicago", SlotResolution.ShiftedPastGap));

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"schedule\":{\"floor\":\"last_interaction\",\"time_zone_id\":\"America/Chicago\",\"slot\":\"shifted_past_gap\"}", json);
    }

    // A record with no message was never scheduled, so there is no send to explain. Same rule
    // the action plan follows on a record the consent gate suppressed.
    [Fact]
    public void Serializes_AbsentSchedule_AsNull()
    {
        var diagnostics = new AgentDiagnostics(true, null, false, 0, SuppressionReason.NoContactConsent);

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"schedule\":null", json);
    }

    // D24 and playbook step 57: the diagnostics name the implementation that wrote the
    // message and how many compose calls it took, so a run that quietly fell back to the
    // offline composer reads differently from one the model answered first time.
    [Fact]
    public void Serializes_Composition_WithComposerAndAttempts()
    {
        var diagnostics = new AgentDiagnostics(
            true,
            true,
            true,
            0,
            SuppressionReason.None,
            new ActionPlanNotes(HorizonBranch.Short, 32, ActionSource.CatalogRow),
            new ScheduleNotes(ScheduleFloor.ReferenceTime, "America/Chicago", SlotResolution.Exact),
            new CompositionNotes(ComposerNames.Template, Attempts: 3, LocaleApplied: true));

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"composition\":{\"composer\":\"template\",\"attempts\":3,\"locale_applied\":true}", json);
    }

    // A record the consent gate suppressed has no message, so no implementation wrote one.
    [Fact]
    public void Serializes_AbsentComposition_AsNull()
    {
        var diagnostics = new AgentDiagnostics(true, null, false, 0, SuppressionReason.NoContactConsent);

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"composition\":null", json);
    }
}
