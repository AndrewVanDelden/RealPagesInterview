using System.Text.Json.Serialization;
using Agent.Common;

namespace Agent.Safety;

// The four safety checks (D38). They are named as the checks themselves rather than as one
// PII check, because D40 gives SocialSecurityNumber and LongDigitRun different answers to
// the question of whether a record may switch them off.
//
// The converter is attached here rather than at a property, for the reason BrandStyleRule
// gives: these values reach the wire as a member of the elements of a list (the review queue
// of D43), and a property-level converter converts the property's own type, not its elements.
[JsonConverter(typeof(SnakeCaseLowerEnumConverter<SafetyCheck>))]
public enum SafetyCheck
{
    OptOutInstructions,
    SocialSecurityNumber,
    LongDigitRun,
    FairHousing,
}
