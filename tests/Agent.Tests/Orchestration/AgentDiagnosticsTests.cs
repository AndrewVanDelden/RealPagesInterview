using System.Text.Json;
using Agent.Common;
using Agent.Composition;
using Agent.Decisions;
using Agent.Orchestration;
using Agent.Safety;
using Xunit;

namespace Agent.Tests.Orchestration;

public class AgentDiagnosticsTests
{
    private static readonly IReadOnlyDictionary<string, RequiredStateVerdict> NoStates =
        new Dictionary<string, RequiredStateVerdict>(StringComparer.Ordinal);

    // D3: the suppression reason is spelled in snake_case on the wire, the same spelling
    // next_action.reason uses, so a diagnostics row and an output row read alike.
    [Fact]
    public void Serializes_SuppressionReason_InSnakeCase()
    {
        var diagnostics = new AgentDiagnostics(NoStates, 0, SuppressionReason: SuppressionReason.NoContactConsent);

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"suppression_reason\":\"no_contact_consent\"", json);
    }

    [Fact]
    public void Serializes_DefaultSuppressionReason_AsNone()
    {
        var diagnostics = new AgentDiagnostics(NoStates, 0);

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"suppression_reason\":\"none\"", json);
    }

    // D42: the answer to assertions.required_states. The keys are the record's own strings and
    // are written verbatim - AgentJsonOptions sets PropertyNamingPolicy and not
    // DictionaryKeyPolicy - so an answer can be traced back to the assertion that asked for it.
    // The verdicts are spelled the way every other enum on the wire is (D3).
    [Fact]
    public void Serializes_RequiredStates_WithVerbatimKeysAndSnakeCaseVerdicts()
    {
        var diagnostics = new AgentDiagnostics(
            new Dictionary<string, RequiredStateVerdict>(StringComparer.Ordinal)
            {
                ["consent_verified"] = RequiredStateVerdict.Earned,
                ["fair_housing_check_passed"] = RequiredStateVerdict.NotEvaluated,
                ["brand_style_applied"] = RequiredStateVerdict.NotEarned,
                ["renewal_offer_loaded"] = RequiredStateVerdict.NoCheckDefined,
            },
            0);

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains(
            "\"required_states\":{\"consent_verified\":\"earned\",\"fair_housing_check_passed\":\"not_evaluated\","
            + "\"brand_style_applied\":\"not_earned\",\"renewal_offer_loaded\":\"no_check_defined\"}",
            json);
    }

    // A record that asserts no state asked no question, so the answer is an empty object, not
    // a null. Null would be indistinguishable from a run that never built the map.
    [Fact]
    public void Serializes_NoRequiredStates_AsAnEmptyObject()
    {
        var diagnostics = new AgentDiagnostics(NoStates, 0);

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"required_states\":{}", json);
    }

    // D42: a diagnostic that says only "false" tells a reader nothing, so the row names the
    // rules that failed. The rule names are spelled in snake_case the way every other enum on
    // the wire is; the converter is on BrandStyleRule itself because these reach the wire as
    // list elements.
    [Fact]
    public void Serializes_BrandStyleFailures_InSnakeCase()
    {
        var diagnostics = new AgentDiagnostics(
            NoStates,
            0,
            [BrandStyleRule.OptOutOnLastLine, BrandStyleRule.SubjectMatchesChannel]);

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"brand_style_failures\":[\"opt_out_on_last_line\",\"subject_matches_channel\"]", json);
    }

    // Empty and null are different facts: an empty list is a message that was checked and
    // broke no rule, null is a record with no message to check at all.
    [Fact]
    public void Serializes_NoBrandStyleFailures_AsAnEmptyListAndAnUncheckedRecordAsNull()
    {
        string checkedAndClean = JsonSerializer.Serialize(new AgentDiagnostics(NoStates, 0, []), AgentJsonOptions.Default);
        string neverChecked = JsonSerializer.Serialize(new AgentDiagnostics(NoStates, 0), AgentJsonOptions.Default);

        Assert.Contains("\"brand_style_failures\":[]", checkedAndClean);
        Assert.Contains("\"brand_style_failures\":null", neverChecked);
    }

    // D18 and the Phase 3 check: the diagnostics say which horizon branch the record took,
    // how many days that was, and which catalog row answered. Both enums are spelled the
    // way every other enum on the wire is (D3).
    [Fact]
    public void Serializes_ActionPlan_WithSnakeCaseBranchAndSource()
    {
        var diagnostics = new AgentDiagnostics(
            NoStates,
            0,
            ActionPlan: new ActionPlanNotes(HorizonBranch.Long, 68, ActionSource.GenericRowNoBranch));

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"action_plan\":{\"branch\":\"long\",\"horizon_days\":68,\"source\":\"generic_row_no_branch\"}", json);
    }

    // The planner never ran on a record with no consented channel, so there is no plan to
    // explain and the diagnostics say nothing rather than reporting a branch nobody took.
    [Fact]
    public void Serializes_AbsentActionPlan_AsNull()
    {
        var diagnostics = new AgentDiagnostics(NoStates, 0, SuppressionReason: SuppressionReason.NoContactConsent);

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
            NoStates,
            0,
            ActionPlan: new ActionPlanNotes(HorizonBranch.Short, 32, ActionSource.CatalogRow),
            Schedule: new ScheduleNotes(ScheduleFloor.LastInteraction, "America/Chicago", SlotResolution.ShiftedPastGap));

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"schedule\":{\"floor\":\"last_interaction\",\"time_zone_id\":\"America/Chicago\",\"slot\":\"shifted_past_gap\"}", json);
    }

    // A record with no message was never scheduled, so there is no send to explain. Same rule
    // the action plan follows on a record with no consented channel.
    [Fact]
    public void Serializes_AbsentSchedule_AsNull()
    {
        var diagnostics = new AgentDiagnostics(NoStates, 0, SuppressionReason: SuppressionReason.NoContactConsent);

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"schedule\":null", json);
    }

    // D24 and playbook step 57: the diagnostics name the implementation that wrote the
    // message and how many compose calls it took, so a run that quietly fell back to the
    // offline composer reads differently from one the model answered first time. D66 leaves
    // these three here and moves nothing else in: they describe a returned message, and a
    // record with no message has no answer to any of them.
    [Fact]
    public void Serializes_Composition_WithComposerAndAttempts()
    {
        var diagnostics = new AgentDiagnostics(
            NoStates,
            0,
            ActionPlan: new ActionPlanNotes(HorizonBranch.Short, 32, ActionSource.CatalogRow),
            Schedule: new ScheduleNotes(ScheduleFloor.ReferenceTime, "America/Chicago", SlotResolution.Exact),
            Composition: new CompositionNotes(ComposerNames.Template, Attempts: 3, LocaleApplied: true));

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains(
            "\"composition\":{\"composer\":\"template\",\"attempts\":3,\"locale_applied\":true}",
            json);
    }

    // D62's and D28's first state on the wire, at the level D66 moved them to: no model call
    // was made, so both members are null and not a row of zeros. Null is the absence of a
    // measurement; zero would be one.
    [Fact]
    public void Serializes_ARecordThatMadeNoModelCall_AsANullModelCostAndNullRetries()
    {
        var diagnostics = new AgentDiagnostics(
            NoStates,
            0,
            Composition: new CompositionNotes(ComposerNames.Template, Attempts: 1, LocaleApplied: true));

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"model_cost\":null,\"network_retries\":null}", json);
    }

    // D62's second and third states on the wire, in the shape a reader of the diagnostics file
    // sees them: two calls, one of them abandoned at its timeout, and the tokens the one that
    // completed reported. D66 puts both counts at the row's own level, after composition, so a
    // suppressed record with no composition object still carries them.
    [Fact]
    public void Serializes_ModelCost_WithEveryCountItMeasured()
    {
        var diagnostics = new AgentDiagnostics(
            NoStates,
            0,
            Composition: new CompositionNotes(ComposerNames.OpenAi, Attempts: 2, LocaleApplied: true),
            ModelCost: new ModelCostNotes(Calls: 2, CompletedCalls: 1, InputTokens: 11, OutputTokens: 7),
            NetworkRetries: 1);

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains(
            "\"model_cost\":{\"calls\":2,\"completed_calls\":1,\"input_tokens\":11,\"output_tokens\":7},\"network_retries\":1}",
            json);
    }

    // The record D66 is about, on the wire: it made two calls and ships nothing, so it has no
    // composition object and the two counts are still there beside it.
    [Fact]
    public void Serializes_ASuppressedRecordThatCalledTheModel_WithNoCompositionAndItsCostIntact()
    {
        var diagnostics = new AgentDiagnostics(
            NoStates,
            1,
            SuppressionReason: SuppressionReason.SafetyViolation,
            ModelCost: new ModelCostNotes(Calls: 2, CompletedCalls: 0, InputTokens: 0, OutputTokens: 0));

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains(
            "\"composition\":null,\"model_cost\":{\"calls\":2,\"completed_calls\":0,\"input_tokens\":0,\"output_tokens\":0},\"network_retries\":null}",
            json);
    }

    // A record with no consented channel has no message, so no implementation wrote one.
    [Fact]
    public void Serializes_AbsentComposition_AsNull()
    {
        var diagnostics = new AgentDiagnostics(NoStates, 0, SuppressionReason: SuppressionReason.NoContactConsent);

        string json = JsonSerializer.Serialize(diagnostics, AgentJsonOptions.Default);

        Assert.Contains("\"composition\":null", json);
    }
}
