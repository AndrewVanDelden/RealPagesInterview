using System.Text.Json;
using System.Text.Json.Serialization;
using Agent.Decisions;

namespace Agent.Orchestration;

// D3 and D18: how next_action was reached, for the diagnostics file. One object that is
// present or absent, rather than three members that could disagree: a record the consent
// gate suppressed never reached the planner, so it has no branch, no horizon, and no row.
// HorizonDays is null when the record states no move date, which is unstated, not zero.
// The converters are attached here rather than to the enums themselves: HorizonBranch and
// ActionSource are decision values, and how the diagnostics file spells them is this
// record's business.
public sealed record ActionPlanNotes(
    [property: JsonConverter(typeof(HorizonBranchConverter))] HorizonBranch Branch,
    int? HorizonDays,
    [property: JsonConverter(typeof(ActionSourceConverter))] ActionSource Source);

// Both enums are written in the same snake_case the rest of the wire uses (D3). System.Text
// .Json applies a naming policy to enum names only through a converter, and a converter is
// only usable as an attribute when it has a parameterless constructor, which is what these
// two subclasses supply.
public sealed class HorizonBranchConverter : JsonStringEnumConverter<HorizonBranch>
{
    public HorizonBranchConverter()
        : base(JsonNamingPolicy.SnakeCaseLower)
    {
    }
}

public sealed class ActionSourceConverter : JsonStringEnumConverter<ActionSource>
{
    public ActionSourceConverter()
        : base(JsonNamingPolicy.SnakeCaseLower)
    {
    }
}
