using System.Text.Json.Serialization;
using Agent.Common;

namespace Agent.Orchestration;

// One required state's answer (A14). Four values rather than a bool, because a bool cannot
// say that this program has no check for a name, and the list is free text a record can fill
// with any string. Only Earned claims the state holds; the other three are different reasons
// it is not claimed, and one name for three facts would hide which (HSC). Deliberately neither
// SafetyCheckVerdict nor Agent.Evaluation.CheckResult: SafetyCheckVerdict answers for a check
// this program owns ("the record said this check does not apply"), while the last two values
// here answer for a name the record chose, which the program may not own at all.
[JsonConverter(typeof(SnakeCaseLowerEnumConverter<RequiredStateVerdict>))]
public enum RequiredStateVerdict
{
    // The step that proves this state ran and proved it.
    Earned,
    // The step ran and did not prove it (a failed FairHousing check, a broken brand rule).
    NotEarned,
    // A step for this state exists but did not run: no consented channel, or composition
    // failed, so there was no message to check. Not a pass, the rule A15 states for the scorer.
    NotEvaluated,
    // No step proves this name, such as the hold-out's renewal_offer_loaded: no labeled record
    // says what would earn it, so a check for it would be a guess recorded as a verdict.
    NoCheckDefined,
}
