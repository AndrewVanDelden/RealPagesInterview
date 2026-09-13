using Agent.Common;
using Agent.Decisions;
using Agent.Domain;
using Agent.Safety;

namespace Agent.Orchestration;

// One row of the review queue: a record a person has to look at, and every reason why. A draft
// the safety gate refused carries its violations and the draft; an action the generic row chose
// carries what had no row and the action given. A record that is both is one row with both
// reasons. Violations and Draft keep their place first and are null on a row not refused on
// safety. The channel is read from the draft, so no second copy of it can disagree.
//
// This file carries prospect text by design: it is a file a reviewer opens, not a log line, and
// the redaction rule is about logs.
public sealed record ReviewQueueEntry(
    string TaskId,
    IReadOnlyList<SafetyViolation>? Violations,
    NextMessage? Draft,
    IReadOnlyList<ReviewReason> Reasons,
    GenericRowAnswer? GenericRow)
{
    // The one place that decides whether a record is queued, so the reasons and the members they
    // describe are built together and cannot disagree. No action plan means no consented
    // channel: the planner never ran and there is no action to review. O(1).
    public static Option<ReviewQueueEntry> For(ProspectCase prospectCase, AgentRunResult result)
    {
        List<ReviewReason> reasons = [];
        RejectedDraft? rejectedDraft = result.RejectedDraft;

        if (rejectedDraft is not null)
        {
            reasons.Add(ReviewReason.SafetyViolation);
        }

        GenericRowAnswer? genericRow = null;
        ActionPlanNotes? actionPlan = result.Diagnostics.ActionPlan;

        if (actionPlan is not null && actionPlan.Source != ActionSource.CatalogRow)
        {
            bool branchMissing = actionPlan.Source == ActionSource.GenericRowNoBranch;
            reasons.Add(branchMissing ? ReviewReason.GenericRowNoBranch : ReviewReason.GenericRowNoMatch);
            genericRow = new GenericRowAnswer(
                prospectCase.Persona,
                prospectCase.LifecycleStage,
                branchMissing ? actionPlan.Branch : null,
                result.Output.NextAction);
        }

        return reasons.Count == 0
            ? Option<ReviewQueueEntry>.None()
            : Option<ReviewQueueEntry>.Some(new ReviewQueueEntry(prospectCase.TaskId, rejectedDraft?.Violations, rejectedDraft?.Message, reasons, genericRow));
    }
}
