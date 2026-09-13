using System.Text.Json.Serialization;
using Agent.Common;
using Agent.Decisions;

namespace Agent.Orchestration;

// How next_action was reached, for the diagnostics file. One object that is
// present or absent, rather than three members that could disagree: a record with no
// consented channel never reached the planner, so it has no branch, no horizon, and no row.
// HorizonDays is null when the record states no move date, which is unstated, not zero.
// The converter is attached here rather than to the enums themselves: HorizonBranch and
// ActionSource are decision values, and how the diagnostics file spells them is this
// record's business.
public sealed record ActionPlanNotes(
    [property: JsonConverter(typeof(SnakeCaseLowerEnumConverter<HorizonBranch>))] HorizonBranch Branch,
    int? HorizonDays,
    [property: JsonConverter(typeof(SnakeCaseLowerEnumConverter<ActionSource>))] ActionSource Source);
