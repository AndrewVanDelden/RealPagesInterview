using Agent.Composition;
using Agent.Safety;

namespace Agent.Orchestration;

// RequiredStates is the answer to the record's own assertions.required_states (D42): one
// verdict per name the record asserts, from the source D42 names for it, and NoCheckDefined
// for any other name. It replaces the ConsentVerified, FairHousingCheckPassed and
// BrandStyleApplied booleans rather than sitting beside them, for three reasons: a bool and a
// map entry are two spellings of one fact that a `with` copy can desynchronize; two of the
// three booleans had no reader outside the tests (LC); and bool? cannot say that this program
// has no check for a name, which is the fact A14 is about. A record that asserts nothing gets
// an empty map, which is the complete answer to a list with no items.
//
// BrandStyleFailures names the rules a message broke, because a diagnostic that says only
// "not earned" tells a reader nothing (D42). Empty is a message that was checked and broke no
// rule; null is a record with no message to check, which is no consented channel or a
// composition failure. Brand style never suppresses (D39), so a record can carry a failed
// rule and a sent message at once.
//
// SafetyViolationCount is every failed safety check's detail lines, so a message matching six
// protected-class terms counts six (D38). SuppressionReason says why a record carries no
// message (D3); None means a message was sent. ActionPlan is how next_action was reached
// (D18) and is null on a record the channel selector returned no value for (D57), where the
// planner never ran. Schedule is how send_at was reached (D22) and is null on a record the
// scheduler never ran for: no consented channel, or a composer that produced no message to
// schedule. A record suppressed by the final safety check keeps its schedule, the way it
// keeps its action plan. Composition is which implementation wrote the message and how many
// compose calls it took (D24, playbook step 57); it is null on a record that has no message,
// which is no consented channel or a composition failure, and suppression_reason already
// separates those two.
public sealed record AgentDiagnostics(
    IReadOnlyDictionary<string, RequiredStateVerdict> RequiredStates,
    int SafetyViolationCount,
    IReadOnlyList<BrandStyleRule>? BrandStyleFailures = null,
    SuppressionReason SuppressionReason = SuppressionReason.None,
    ActionPlanNotes? ActionPlan = null,
    ScheduleNotes? Schedule = null,
    CompositionNotes? Composition = null);
