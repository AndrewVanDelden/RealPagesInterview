using System.Text.Json;
using Agent.Common;
using Agent.Decisions;
using Agent.Domain;
using Agent.Orchestration;
using Agent.Safety;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Orchestration;

// The queue is a work list with two kinds of row: a draft the safety gate refused, and an
// action the generic row chose because no catalog row covered the record. A record that is
// both gets one row naming both, and a record that is neither, or never reached the planner,
// gets none.
public class ReviewQueueEntryTests
{
    private static readonly NextAction GenericAction = new(ActionTypes.FollowUpInDays, Value: 3);

    private static readonly NextMessage Draft = new(CommunicationChannel.Sms, Body: "We heard you're looking in families only.");

    private static readonly IReadOnlyList<SafetyViolation> Violations =
        [new SafetyViolation(SafetyCheck.FairHousing, "Body contains protected-class or steering language: 'families only'.")];

    private static readonly ProspectCase Unseen = SampleProspectCases.Minimal(persona: "guarantor", lifecycleStage: "screening");

    private static AgentRunResult Run(ActionPlanNotes? actionPlan, RejectedDraft? rejectedDraft = null) =>
        new(
            new AgentOutput(rejectedDraft is null ? Draft : new NextMessage(CommunicationChannel.None), GenericAction),
            new AgentDiagnostics(new Dictionary<string, RequiredStateVerdict>(), Violations.Count, ActionPlan: actionPlan),
            rejectedDraft);

    [Fact]
    public void For_CatalogRowAnsweredAndTheMessageWentOut_HasNoEntry()
    {
        Option<ReviewQueueEntry> entry = ReviewQueueEntry.For(SampleProspectCases.Minimal(), Run(new ActionPlanNotes(HorizonBranch.Short, 32, ActionSource.CatalogRow)));

        Assert.False(entry.HasValue);
    }

    // No consented channel: the planner never ran, so there is no action to review.
    [Fact]
    public void For_NoActionPlan_HasNoEntry()
    {
        Option<ReviewQueueEntry> entry = ReviewQueueEntry.For(Unseen, Run(actionPlan: null));

        Assert.False(entry.HasValue);
    }

    // No row for the pair: the pair is what is missing, so the branch is not named.
    [Fact]
    public void For_GenericRowNoMatch_NamesThePersonaStageAndAction()
    {
        ReviewQueueEntry entry = ReviewQueueEntry.For(Unseen, Run(new ActionPlanNotes(HorizonBranch.Long, null, ActionSource.GenericRowNoMatch))).Value;

        Assert.Equal(Unseen.TaskId, entry.TaskId);
        Assert.Equal([ReviewReason.GenericRowNoMatch], entry.Reasons);
        Assert.Equal(new GenericRowAnswer("guarantor", "screening", null, GenericAction), entry.GenericRow);
        Assert.Null(entry.Violations);
        Assert.Null(entry.Draft);
    }

    // A row for the pair with no action for this branch: the branch is what is missing.
    [Fact]
    public void For_GenericRowNoBranch_NamesTheBranchToo()
    {
        ProspectCase open = SampleProspectCases.Minimal(lifecycleStage: "open");

        ReviewQueueEntry entry = ReviewQueueEntry.For(open, Run(new ActionPlanNotes(HorizonBranch.Short, 12, ActionSource.GenericRowNoBranch))).Value;

        Assert.Equal([ReviewReason.GenericRowNoBranch], entry.Reasons);
        Assert.Equal(new GenericRowAnswer("prospect", "open", HorizonBranch.Short, GenericAction), entry.GenericRow);
    }

    [Fact]
    public void For_SafetySuppressionOnACatalogRow_CarriesTheDraftAndOneReason()
    {
        ReviewQueueEntry entry = ReviewQueueEntry.For(
            SampleProspectCases.Minimal(),
            Run(new ActionPlanNotes(HorizonBranch.Short, 32, ActionSource.CatalogRow), new RejectedDraft(Draft, Violations))).Value;

        Assert.Equal([ReviewReason.SafetyViolation], entry.Reasons);
        Assert.Same(Violations, entry.Violations);
        Assert.Same(Draft, entry.Draft);
        Assert.Null(entry.GenericRow);
    }

    [Fact]
    public void For_SafetySuppressionAnsweredByTheGenericRow_IsOneEntryWithBothReasons()
    {
        ReviewQueueEntry entry = ReviewQueueEntry.For(
            Unseen,
            Run(new ActionPlanNotes(HorizonBranch.Short, 32, ActionSource.GenericRowNoMatch), new RejectedDraft(Draft, Violations))).Value;

        Assert.Equal([ReviewReason.SafetyViolation, ReviewReason.GenericRowNoMatch], entry.Reasons);
        Assert.Same(Draft, entry.Draft);
        Assert.Equal(new GenericRowAnswer("guarantor", "screening", null, GenericAction), entry.GenericRow);
    }

    // The members a safety row always had keep their names and their order, and the new ones
    // follow them, so a reader of an existing row finds it unchanged apart from the additions.
    [Fact]
    public void Serialized_SafetyRow_KeepsTheExistingMembersFirstAndAppendsTheNewOnes()
    {
        ReviewQueueEntry entry = ReviewQueueEntry.For(
            SampleProspectCases.Minimal(),
            Run(new ActionPlanNotes(HorizonBranch.Short, 32, ActionSource.CatalogRow), new RejectedDraft(Draft, Violations))).Value;

        using JsonDocument row = JsonDocument.Parse(JsonSerializer.Serialize(entry, AgentJsonOptions.Default));

        Assert.Equal(
            ["task_id", "violations", "draft", "reasons", "generic_row"],
            row.RootElement.EnumerateObject().Select(member => member.Name).ToArray());
        Assert.Equal("[\"safety_violation\"]", row.RootElement.GetProperty("reasons").GetRawText());
        Assert.Equal(JsonValueKind.Null, row.RootElement.GetProperty("generic_row").ValueKind);
    }

    [Fact]
    public void Serialized_GenericRow_SpellsTheReasonAndTheBranchInSnakeCase()
    {
        ProspectCase open = SampleProspectCases.Minimal(lifecycleStage: "open");
        ReviewQueueEntry noBranch = ReviewQueueEntry.For(open, Run(new ActionPlanNotes(HorizonBranch.Short, 12, ActionSource.GenericRowNoBranch))).Value;
        ReviewQueueEntry noMatch = ReviewQueueEntry.For(Unseen, Run(new ActionPlanNotes(HorizonBranch.Long, null, ActionSource.GenericRowNoMatch))).Value;

        using JsonDocument noBranchRow = JsonDocument.Parse(JsonSerializer.Serialize(noBranch, AgentJsonOptions.Default));
        using JsonDocument noMatchRow = JsonDocument.Parse(JsonSerializer.Serialize(noMatch, AgentJsonOptions.Default));

        Assert.Equal("[\"generic_row_no_branch\"]", noBranchRow.RootElement.GetProperty("reasons").GetRawText());
        Assert.Equal(
            "{\"persona\":\"prospect\",\"lifecycle_stage\":\"open\",\"branch\":\"short\",\"action\":{\"type\":\"follow_up_in_days\",\"value\":3}}",
            noBranchRow.RootElement.GetProperty("generic_row").GetRawText());
        Assert.Equal("[\"generic_row_no_match\"]", noMatchRow.RootElement.GetProperty("reasons").GetRawText());
        Assert.Equal(
            "{\"persona\":\"guarantor\",\"lifecycle_stage\":\"screening\",\"branch\":null,\"action\":{\"type\":\"follow_up_in_days\",\"value\":3}}",
            noMatchRow.RootElement.GetProperty("generic_row").GetRawText());
        Assert.Equal(JsonValueKind.Null, noMatchRow.RootElement.GetProperty("violations").ValueKind);
        Assert.Equal(JsonValueKind.Null, noMatchRow.RootElement.GetProperty("draft").ValueKind);
    }
}
