using Agent.Composition;
using Agent.Safety;

namespace Agent.Orchestration;

// One diagnostics row: how every decision on one record was reached. A null member is a step
// that never ran or a value never measured, never a pass and never a measured zero.
public sealed record AgentDiagnostics(
    // The answer to the record's own assertions.required_states: one verdict per asserted name
    // from the step that proves it, NoCheckDefined for any other name (A14), and an empty map for
    // a record that asserts nothing. A map rather than booleans: bool? cannot say "no check for
    // this name", and a bool beside a map entry is two spellings a `with` copy can desynchronize.
    IReadOnlyDictionary<string, RequiredStateVerdict> RequiredStates,
    // Every failed safety check's detail lines, so six protected-class terms count six.
    int SafetyViolationCount,
    // The rules a message broke, since "not earned" alone tells a reader nothing. Empty is a
    // message checked and clean; null is no message to check (no consented channel, or a
    // composition failure). Brand style never suppresses, so a failed rule and a sent message
    // can appear on one record.
    IReadOnlyList<BrandStyleRule>? BrandStyleFailures = null,
    // Why the record carries no message; None means a message was sent.
    SuppressionReason SuppressionReason = SuppressionReason.None,
    // How next_action was reached; null where the channel selector returned no value, so the
    // planner never ran.
    ActionPlanNotes? ActionPlan = null,
    // How send_at was reached; null where the scheduler never ran: no consented channel, or no
    // message to schedule. A record suppressed by the final safety check keeps it, as it keeps
    // its action plan.
    ScheduleNotes? Schedule = null,
    // Which implementation wrote the message and how many compose calls it took (playbook step
    // 57); null on a record with no message, and suppression_reason tells those cases apart.
    CompositionNotes? Composition = null,
    // What the run spent: token counts and transport retries. Here rather than in Composition,
    // which is null on a refused draft and a composition failure, though both were billed for
    // their calls. Null is no measurement, never a measured zero. Last, so the key order of
    // every existing diagnostics row is untouched.
    ModelCostNotes? ModelCost = null,
    int? NetworkRetries = null);
