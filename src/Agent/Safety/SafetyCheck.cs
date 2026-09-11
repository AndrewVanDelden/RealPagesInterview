using System.Text.Json.Serialization;
using Agent.Common;

namespace Agent.Safety;

// The four safety checks, each answering for itself. Named as the checks themselves rather
// than one PII check, because a record can never switch SocialSecurityNumber off but can
// switch LongDigitRun off.
//
// The converter is attached here rather than at a property, for the reason BrandStyleRule
// gives: these values reach the wire as a member of the elements of a list (the review
// queue), and a property-level converter converts the property's own type, not its elements.
[JsonConverter(typeof(SnakeCaseLowerEnumConverter<SafetyCheck>))]
public enum SafetyCheck
{
    OptOutInstructions,
    SocialSecurityNumber,
    LongDigitRun,
    FairHousing,
}
