using Agent.Common;
using Agent.Decisions;
using Agent.Domain;
using Xunit;

namespace Agent.Tests.Decisions;

// A7: horizon is move_date_target minus the reference date (a value the caller passes, never a
// clock), counted in the record's timezone by the caller. At most 45 days is short, and a past
// date is short; more than 45 days is long; an absent date is its own branch, because a prospect
// whose timeline is not stated has not been qualified on it. The branch then picks the action out
// of the catalog keyed on persona and lifecycle stage, and the planner returns the why beside the
// what.
public class NextActionPlannerTests
{
    private static readonly NextActionPlanner Planner = new();
    private static readonly DateOnly ReferenceDate = new(2025, 12, 9);

    // Sample 1: prospect at new, move date 2026-01-10, 32 days out.
    [Fact]
    public void Plan_ShortHorizonSampleCase_ReturnsStartCadenceFromItsRow()
    {
        PlannedAction planned = Planner.Plan("prospect", "new", new DateOnly(2026, 1, 10), ReferenceDate);

        Assert.Equal(ActionTypes.StartCadence, planned.Action.Type);
        Assert.Equal("prospect_welcome_short_horizon", planned.Action.Name);
        Assert.Null(planned.Action.Value);
        Assert.Equal(HorizonBranch.Short, planned.Branch);
        Assert.Equal(32, planned.HorizonDays);
        Assert.Equal(ActionSource.CatalogRow, planned.Source);
    }

    // Sample 2: prospect at open, move date 2026-02-15, 68 days out.
    [Fact]
    public void Plan_LongHorizonSampleCase_ReturnsFollowUpInDaysFromItsRow()
    {
        PlannedAction planned = Planner.Plan("prospect", "open", new DateOnly(2026, 2, 15), ReferenceDate);

        Assert.Equal(ActionTypes.FollowUpInDays, planned.Action.Type);
        Assert.Null(planned.Action.Name);
        Assert.Equal(3, planned.Action.Value);
        Assert.Equal(HorizonBranch.Long, planned.Branch);
        Assert.Equal(68, planned.HorizonDays);
        Assert.Equal(ActionSource.CatalogRow, planned.Source);
    }

    [Fact]
    public void Plan_ExactlyAtThreshold_IsShort()
    {
        PlannedAction planned = Planner.Plan("prospect", "new", ReferenceDate.AddDays(NextActionPlanner.ShortHorizonThresholdDays), ReferenceDate);

        Assert.Equal(HorizonBranch.Short, planned.Branch);
        Assert.Equal(ActionTypes.StartCadence, planned.Action.Type);
    }

    // The long-horizon cadence the hold-out names is the long branch's: its name states the
    // horizon it is for, and a prospect whose move is months away belongs in a nurture sequence.
    [Fact]
    public void Plan_OneDayPastThreshold_IsLong()
    {
        PlannedAction planned = Planner.Plan("prospect", "new", ReferenceDate.AddDays(NextActionPlanner.ShortHorizonThresholdDays + 1), ReferenceDate);

        Assert.Equal(HorizonBranch.Long, planned.Branch);
        Assert.Equal(new NextAction(ActionTypes.StartCadence, "prospect_welcome_long_horizon"), planned.Action);
        Assert.Equal(ActionSource.CatalogRow, planned.Source);
    }

    // The horizon is not zero, it is unstated, so the decision object carries null rather than a
    // number nothing measured, and the branch says no date was given.
    [Fact]
    public void Plan_AbsentMoveDate_IsTheNoMoveDateBranchWithNoHorizonDays()
    {
        PlannedAction planned = Planner.Plan("prospect", "new", null, ReferenceDate);

        Assert.Equal(HorizonBranch.NoMoveDate, planned.Branch);
        Assert.Null(planned.HorizonDays);
        Assert.Equal(new NextAction(ActionTypes.StartCadence, "prospect_welcome_long_horizon"), planned.Action);
        Assert.Equal(ActionSource.CatalogRow, planned.Source);
    }

    // A prospect at open with no move date has not been qualified on timeline, and the hold-out
    // follows up sooner, in 2 days, than sample 2's dated record does in 3.
    [Fact]
    public void Plan_ProspectOpenWithNoMoveDate_FollowsUpInTwoDays()
    {
        PlannedAction planned = Planner.Plan("prospect", "open", null, ReferenceDate);

        Assert.Equal(new NextAction(ActionTypes.FollowUpInDays, Value: 2), planned.Action);
        Assert.Equal(HorizonBranch.NoMoveDate, planned.Branch);
        Assert.Equal(ActionSource.CatalogRow, planned.Source);
    }

    [Fact]
    public void Plan_MoveDateInThePast_IsShortWithANegativeHorizon()
    {
        PlannedAction planned = Planner.Plan("prospect", "new", new DateOnly(2025, 11, 1), ReferenceDate);

        Assert.Equal(HorizonBranch.Short, planned.Branch);
        Assert.Equal(-38, planned.HorizonDays);
        Assert.Equal(ActionTypes.StartCadence, planned.Action.Type);
    }

    // A8: a persona the samples never named takes the generic row, and the planner says so
    // rather than reaching for the nearest row that happens to exist: the lookup is the exact
    // key, then the generic row, and nothing else.
    [Fact]
    public void Plan_PersonaWithNoRow_UsesTheGenericRowAndRecordsIt()
    {
        PlannedAction planned = Planner.Plan("resident", "renewal", new DateOnly(2026, 1, 10), ReferenceDate);

        Assert.Equal(ActionTypes.StartCadence, planned.Action.Type);
        Assert.Equal(ActionSource.GenericRowNoMatch, planned.Source);
    }

    // No record showed the generic row a no-move-date value, so it answers that branch with its
    // long action.
    [Fact]
    public void Plan_NoRowAndNoMoveDate_UsesTheGenericRowsLongAction()
    {
        PlannedAction planned = Planner.Plan("resident", "renewal", null, ReferenceDate);

        Assert.Equal(new NextAction(ActionTypes.FollowUpInDays, Value: 3), planned.Action);
        Assert.Equal(HorizonBranch.NoMoveDate, planned.Branch);
        Assert.Equal(ActionSource.GenericRowNoMatch, planned.Source);
    }

    // prospect/open was only ever seen on a long horizon and with no date, so its short branch
    // has no evidence and comes from the generic row instead.
    [Fact]
    public void Plan_BranchTheRowDoesNotState_UsesTheGenericRowAndRecordsIt()
    {
        PlannedAction planned = Planner.Plan("prospect", "open", new DateOnly(2026, 1, 10), ReferenceDate);

        Assert.Equal(ActionTypes.StartCadence, planned.Action.Type);
        Assert.Equal(ActionSource.GenericRowNoBranch, planned.Source);
    }

    [Fact]
    public void Plan_ReadsTheCatalogItWasGiven()
    {
        ActionCatalog catalog = ActionCatalog.Create(
            new GenericActionRow(
                new NextAction(ActionTypes.NoOp, Reason: "unmapped"),
                new NextAction(ActionTypes.NoOp, Reason: "unmapped")),
            [
                new ActionCatalogRow(
                    "prospect",
                    "new",
                    Option<NextAction>.Some(new NextAction(ActionTypes.FollowUpInDays, Value: 7)),
                    Option<NextAction>.None(),
                    Option<NextAction>.None()),
            ]).Value;

        PlannedAction planned = new NextActionPlanner(catalog).Plan("prospect", "new", new DateOnly(2026, 1, 10), ReferenceDate);

        Assert.Equal(ActionTypes.FollowUpInDays, planned.Action.Type);
        Assert.Equal(7, planned.Action.Value);
    }
}
