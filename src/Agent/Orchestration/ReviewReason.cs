using System.Text.Json.Serialization;
using Agent.Common;

namespace Agent.Orchestration;

// Why a record is on the review queue. A safety violation is a draft the safety gate refused.
// The two generic-row reasons are an action no catalog row stated: no row for the persona and
// stage at all, or a row with no action for the record's horizon branch. Written in snake_case,
// the spelling the diagnostics file uses for the same two sources.
[JsonConverter(typeof(SnakeCaseLowerEnumConverter<ReviewReason>))]
public enum ReviewReason
{
    SafetyViolation,
    GenericRowNoMatch,
    GenericRowNoBranch,
}
