using Agent.Decisions;
using Agent.Domain;
using Xunit;

namespace Agent.Tests.Decisions;

public class NextActionPlannerTests
{
    private static readonly INextActionPlanner Planner = new NextActionPlanner();

    [Fact]
    public void Plan_ShortHorizonSampleCase_ReturnsStartCadence()
    {
        NextAction action = Planner.Plan(new DateOnly(2026, 1, 10), new DateOnly(2025, 12, 8));

        Assert.Equal("start_cadence", action.Type);
        Assert.Equal("prospect_welcome_short_horizon", action.Name);
        Assert.Null(action.Value);
    }

    [Fact]
    public void Plan_LongHorizonSampleCase_ReturnsFollowUpInDays()
    {
        NextAction action = Planner.Plan(new DateOnly(2026, 2, 15), new DateOnly(2025, 12, 6));

        Assert.Equal("follow_up_in_days", action.Type);
        Assert.Null(action.Name);
        Assert.Equal(3, action.Value);
    }

    [Fact]
    public void Plan_ExactlyAtThreshold_ReturnsStartCadence()
    {
        NextAction action = Planner.Plan(new DateOnly(2026, 1, 20), new DateOnly(2025, 12, 6));

        Assert.Equal("start_cadence", action.Type);
    }
}
