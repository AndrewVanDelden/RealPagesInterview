using System.Text.Json.Serialization;
using Agent.Common;

namespace Agent.Orchestration;

// One required state's answer (D42, A14). Four values rather than a bool, because a bool
// cannot say that this program has no check for a name, which is the whole point of the
// not-earned rule: the list is free text and a record can assert any string.
//
// Only Earned is a claim that the state holds. NotEarned, NotEvaluated and NoCheckDefined
// are three different reasons the state is not claimed, and collapsing them would put three
// facts behind one name (HSC):
//
//   Earned          the step that proves this state ran and proved it.
//   NotEarned       the step ran and did not prove it (a failed FairHousing check, a message
//                   that broke a brand rule).
//   NotEvaluated    a step for this state exists but did not run for this record: the consent
//                   gate suppressed it, or composition failed, so there was no message to
//                   check. This is the fact the current bool? spells as null, and it is not a
//                   pass, the same rule A15 states for the scorer.
//   NoCheckDefined  this program has no step that proves this name. The hold-out's
//                   renewal_offer_loaded is exactly such a name and stays here: a rule for it
//                   would be fitted to an evaluation set (D9, A19).
//
// This is deliberately neither SafetyCheckVerdict nor Agent.Evaluation.CheckResult, for the
// reason SafetyCheckVerdict already gives about CheckResult. SafetyCheckVerdict answers for
// a check this program owns, so its third value is NotApplicable, "the record said this check
// does not apply". The third and fourth values here answer for a name the record chose, which
// the program may not own at all; a shared enum would have to mean both.
[JsonConverter(typeof(SnakeCaseLowerEnumConverter<RequiredStateVerdict>))]
public enum RequiredStateVerdict
{
    Earned,
    NotEarned,
    NotEvaluated,
    NoCheckDefined,
}
