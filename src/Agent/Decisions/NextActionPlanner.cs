namespace Agent.Decisions;

// Playbook step 38: inputs in, decision out. No I/O, no clock, no randomness; the reference
// date is a parameter (D10) and the catalog is a value the caller can substitute.
public sealed class NextActionPlanner(ActionCatalog? catalog = null) : INextActionPlanner
{
    // A7 marks this not configurable: the two samples put the boundary anywhere in (32, 68]
    // and 45 is a round number in that range, so a setting here would be one unknown value
    // dressed up as a choice (D20, playbook step 41).
    public const int ShortHorizonThresholdDays = 45;

    private readonly ActionCatalog _catalog = catalog ?? ActionCatalog.Default;

    // A7: horizon is move_date_target minus the reference date, already converted to the
    // record's local date by the caller. At most the threshold is short; an absent date is
    // long (no date, no cadence to start); a past date is short (the move is due).
    public PlannedAction Plan(string? persona, string? lifecycleStage, DateOnly? moveDateTarget, DateOnly referenceDate)
    {
        int? horizonDays = moveDateTarget is { } target ? target.DayNumber - referenceDate.DayNumber : null;

        HorizonBranch branch = horizonDays is { } days && days <= ShortHorizonThresholdDays
            ? HorizonBranch.Short
            : HorizonBranch.Long;

        ActionCatalogMatch match = _catalog.Resolve(persona, lifecycleStage, branch);

        return new PlannedAction(match.Action, branch, horizonDays, match.Source);
    }
}
