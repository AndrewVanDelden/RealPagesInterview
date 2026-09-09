using Agent.Composition;

namespace Agent.Orchestration;

// FairHousingCheckPassed is the SafetyCheck.FairHousing verdict alone (D38), never
// "no violations at all": a message that only omitted its opt-out line is a failure of a
// different check and records no fair-housing failure here. It is null when the safety
// validator never ran for this prospect (no-consent suppression, or the composer could not
// produce a message to validate) - null means "not evaluated", distinct from true/false
// which mean the check ran and recorded a result. SafetyViolationCount is every failed
// check's detail lines, so a message matching six protected-class terms counts six.
// SuppressionReason says why a record carries
// no message (D3); None means a message was sent. ActionPlan is how next_action was
// reached (D18) and is null on a record the consent gate suppressed, where the planner
// never ran. Schedule is how send_at was reached (D22) and is null on a record the
// scheduler never ran for: consent suppression, or a composer that produced no message to
// schedule. A record suppressed by the final safety check keeps its schedule, the way it
// keeps its action plan. Composition is which implementation wrote the message and how many
// compose calls it took (D24, playbook step 57); it is null on a record that has no message,
// which is consent suppression or a composition failure, and suppression_reason already
// separates those two.
public sealed record AgentDiagnostics(
    bool ConsentVerified,
    bool? FairHousingCheckPassed,
    bool BrandStyleApplied,
    int SafetyViolationCount,
    SuppressionReason SuppressionReason = SuppressionReason.None,
    ActionPlanNotes? ActionPlan = null,
    ScheduleNotes? Schedule = null,
    CompositionNotes? Composition = null);
