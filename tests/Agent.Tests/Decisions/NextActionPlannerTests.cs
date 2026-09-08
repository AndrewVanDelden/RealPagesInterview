using Agent.Decisions;
using Agent.Domain;
using Xunit;

namespace Agent.Tests.Decisions;

// A7: horizon is move_date_target minus the reference date (D10), counted in the record's
// timezone by the caller. At most 45 days is short, else long; an absent date is long; a
// past date is short.
public class NextActionPlannerTests
{
    private static readonly INextActionPlanner Planner = new NextActionPlanner();
    private static readonly DateOnly ReferenceDate = new(2025, 12, 9);

    [Fact]
    public void Plan_ShortHorizonSampleCase_ReturnsStartCadence()
    {
        NextAction action = Planner.Plan(new DateOnly(2026, 1, 10), ReferenceDate);

        Assert.Equal("start_cadence", action.Type);
        Assert.Equal("prospect_welcome_short_horizon", action.Name);
        Assert.Null(action.Value);
    }

    [Fact]
    public void Plan_LongHorizonSampleCase_ReturnsFollowUpInDays()
    {
        NextAction action = Planner.Plan(new DateOnly(2026, 2, 15), ReferenceDate);

        Assert.Equal("follow_up_in_days", action.Type);
        Assert.Null(action.Name);
        Assert.Equal(3, action.Value);
    }

    [Fact]
    public void Plan_ExactlyAtThreshold_ReturnsStartCadence()
    {
        NextAction action = Planner.Plan(ReferenceDate.AddDays(45), ReferenceDate);

        Assert.Equal("start_cadence", action.Type);
    }

    [Fact]
    public void Plan_OneDayPastThreshold_ReturnsFollowUpInDays()
    {
        NextAction action = Planner.Plan(ReferenceDate.AddDays(46), ReferenceDate);

        Assert.Equal("follow_up_in_days", action.Type);
    }

    [Fact]
    public void Plan_AbsentMoveDate_ReturnsLongHorizonFollowUp()
    {
        NextAction action = Planner.Plan(null, ReferenceDate);

        Assert.Equal("follow_up_in_days", action.Type);
        Assert.Equal(3, action.Value);
    }

    [Fact]
    public void Plan_MoveDateInThePast_ReturnsStartCadenceInsteadOfThrowing()
    {
        NextAction action = Planner.Plan(new DateOnly(2025, 11, 1), ReferenceDate);

        Assert.Equal("start_cadence", action.Type);
    }

    [Fact]
    public void Plan_CustomShortHorizonThreshold_ChangesClassification()
    {
        var customPlanner = new NextActionPlanner(new NextActionPlannerOptions(shortHorizonThresholdDays: 10));

        NextAction action = customPlanner.Plan(new DateOnly(2026, 1, 10), ReferenceDate);

        Assert.Equal("follow_up_in_days", action.Type);
    }
}
