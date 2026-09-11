using System.Text.Json.Serialization;
using Agent.Common;

namespace Agent.Safety;

// Three brand rules, each fitted only to what the two sample bodies prove and satisfied by the
// template composer on both channels in both language sets; each names itself so a failure says
// which rule broke. Rejected, each for a reason: an sms length cap (sample 1 is 166 characters),
// a sentence count (5 and 3, so any interval is chosen, not observed), "exactly one call to
// action" (both samples carry two asks for one cta.type), first-name and opt-out rules (Evaluator's
// checks restated), a second-person pronoun rule (English only), and "no ALL-CAPS word but STOP"
// (the template fails it on sample 1, emitting TX from city_interest). The converter sits on the
// enum: these reach the wire as list elements, which a property-level converter does not convert.
[JsonConverter(typeof(SnakeCaseLowerEnumConverter<BrandStyleRule>))]
public enum BrandStyleRule
{
    // The opt-out instruction sits on the body's last non-blank line. The keyword half is
    // OptOutInstructions, the one definition the validator enforces and the scorer measures,
    // so the two cannot drift; what this rule adds over them is position.
    OptOutOnLastLine,

    // The body carries at most one exclamation mark (sample 1 one, sample 2 none).
    ExclamationLimit,

    // A subject is present exactly when the channel is email (sample 1 sms null, sample 2
    // email 59 characters).
    SubjectMatchesChannel,
}
