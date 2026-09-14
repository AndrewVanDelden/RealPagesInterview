namespace Agent.Decisions;

// Playbook step 38: inputs in, decision out. No I/O, no clock, no randomness; the reference
// date is a parameter, the run's --now, so a run is reproducible, and the catalog is a value
// the caller can substitute.
public sealed class NextActionPlanner(ActionCatalog? catalog = null)
{
    // A7 marks this not configurable: the two samples put the boundary anywhere in (32, 68]
    // and 45 is a round number in that range, so a setting here would be one unknown value
    // dressed up as a choice (playbook step 41: a setting is earned by a second known value).
    public const int ShortHorizonThresholdDays = 45;

    private readonly ActionCatalog _catalog = catalog ?? ActionCatalog.Default;

    // A7: horizon is move_date_target minus the reference date, already converted to the
    // record's local date by the caller. At most the threshold is short, and a past date is short
    // (the move is due); beyond it is long; an absent date is its own branch, because a timeline
    // nobody stated has not been qualified yet.
    public PlannedAction Plan(string? persona, string? lifecycleStage, DateOnly? moveDateTarget, DateOnly referenceDate)
    {
        int? horizonDays = moveDateTarget is { } target ? target.DayNumber - referenceDate.DayNumber : null;

        HorizonBranch branch = horizonDays switch
        {
            null => HorizonBranch.NoMoveDate,
            <= ShortHorizonThresholdDays => HorizonBranch.Short,
            _ => HorizonBranch.Long,
        };

        ActionCatalogMatch match = _catalog.Resolve(persona, lifecycleStage, branch);

        return new PlannedAction(match.Action, branch, horizonDays, match.Source);
    }
}
