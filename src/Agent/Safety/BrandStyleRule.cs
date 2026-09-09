using System.Text.Json.Serialization;
using Agent.Common;

namespace Agent.Safety;

// The three brand rules of D42, each fitted only to what the two sample bodies prove and
// each satisfied by the template composer on both channels in both language sets. A rule
// names itself so a failing check reports which rule failed, not just that the message was
// off-voice.
//
// What is deliberately absent, because each was proposed and rejected in D42 with a reason:
// an sms length cap (sample 1 is 166 characters), a sentence count (5 and 3, so any interval
// containing both is chosen rather than observed), "exactly one call to action" (both samples
// carry two imperative asks for one cta.type), "the body carries the first name" and "the
// message carries an opt-out" (Evaluator's personalization and opt-out checks restated), a
// second-person pronoun rule (an English rule wearing a general name; the Spanish set has no
// "you" token), and "no ALL-CAPS word other than STOP" (the template composer fails it on
// sample 1's own record, emitting TX from the record's city_interest).
//
// The converter is attached here rather than at the AgentDiagnostics property, which is
// where ActionPlanNotes and ScheduleNotes attach theirs: these values reach the wire as the
// elements of a list, and a property-level converter converts the property's own type, not
// its elements.
[JsonConverter(typeof(SnakeCaseLowerEnumConverter<BrandStyleRule>))]
public enum BrandStyleRule
{
    // The opt-out instruction sits on the body's last non-blank line. The keyword half is
    // OptOutInstructions, the one definition the validator enforces and the scorer measures
    // (D13 b), so the two cannot drift; what this rule adds over them is position.
    OptOutOnLastLine,

    // The body carries at most one exclamation mark (sample 1 one, sample 2 none).
    ExclamationLimit,

    // A subject is present exactly when the channel is email (sample 1 sms null, sample 2
    // email 59 characters).
    SubjectMatchesChannel,
}
