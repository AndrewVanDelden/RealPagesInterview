namespace Agent.Decisions;

// Playbook step 38: inputs in, decision out. No I/O, no clock, no randomness; the reference
// date is a parameter, the run's --now, so a run is reproducible, and the catalog is a value
// the caller can substitute.
public sealed class NextActionPlanner(ActionCatalog? catalog = null)
{
    // A7 marks this not configurable: synthetic_v2's boundary records put it exactly here, 60 days
    // short and 61 long, and the samples' 32 short and 68 long agree.
    public const int ShortHorizonThresholdDays = 60;

    private readonly ActionCatalog _catalog = catalog ?? ActionCatalog.Default;

    // A7: horizon is move_date_target minus the reference date, already converted to the
    // record's local date by the caller. At most the threshold is short and beyond it is long; an
    // absent date is its own branch, because a timeline nobody stated has not been qualified yet, and a
    // past date takes that branch too, because a timeline already passed has to be qualified again.
    // A record on that branch whose call to action is stated takes its row's short action when the row
    // states one: a named call to action is the record's author pushing for the next step now, which is
    // what synthetic_v2's prospect/new records naming book_tour are labeled, where the hold-out's naming
    // none is labeled the long cadence.
    public PlannedAction Plan(string? persona, string? lifecycleStage, DateOnly? moveDateTarget, DateOnly referenceDate, bool callToActionStated = false)
    {
        int? horizonDays = moveDateTarget is { } target ? target.DayNumber - referenceDate.DayNumber : null;

        HorizonBranch branch = horizonDays switch
        {
            null => HorizonBranch.NoMoveDate,
            < 0 => HorizonBranch.NoMoveDate,
            <= ShortHorizonThresholdDays => HorizonBranch.Short,
            _ => HorizonBranch.Long,
        };

        ActionCatalogMatch match = _catalog.Resolve(persona, lifecycleStage, branch);

        if (branch == HorizonBranch.NoMoveDate && callToActionStated)
        {
            ActionCatalogMatch shortMatch = _catalog.Resolve(persona, lifecycleStage, HorizonBranch.Short);

            if (shortMatch.Source == ActionSource.CatalogRow)
            {
                match = shortMatch;
            }
        }

        return new PlannedAction(match.Action, branch, horizonDays, match.Source);
    }
}
