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
        NextAction action = Planner.Plan(new DateOnly(2026, 1, 10), DateTimeOffset.Parse("2025-12-08T15:04:00Z"), "America/Chicago");

        Assert.Equal("start_cadence", action.Type);
        Assert.Equal("prospect_welcome_short_horizon", action.Name);
        Assert.Null(action.Value);
    }

    [Fact]
    public void Plan_LongHorizonSampleCase_ReturnsFollowUpInDays()
    {
        NextAction action = Planner.Plan(new DateOnly(2026, 2, 15), DateTimeOffset.Parse("2025-12-06T11:30:00Z"), "America/Chicago");

        Assert.Equal("follow_up_in_days", action.Type);
        Assert.Null(action.Name);
        Assert.Equal(3, action.Value);
    }

    [Fact]
    public void Plan_ExactlyAtThreshold_ReturnsStartCadence()
    {
        NextAction action = Planner.Plan(new DateOnly(2026, 1, 20), DateTimeOffset.Parse("2025-12-06T12:00:00-06:00"), "America/Chicago");

        Assert.Equal("start_cadence", action.Type);
    }

    [Fact]
    public void Plan_TimeZoneBoundaryCase_UsesProspectLocalDateNotUtcDate()
    {
        // 2025-12-25T02:30:00Z is 2025-12-24 18:30 local in America/Los_Angeles (UTC-8, no DST in December).
        // A naive UTC-based date extraction would use Dec 25 (horizon 45, start_cadence); the correct
        // timezone-aware local date is Dec 24 (horizon 46, follow_up_in_days).
        NextAction action = Planner.Plan(new DateOnly(2026, 2, 8), DateTimeOffset.Parse("2025-12-25T02:30:00Z"), "America/Los_Angeles");

        Assert.Equal("follow_up_in_days", action.Type);
    }

    [Fact]
    public void Plan_MoveDateTargetBeforeLastInteraction_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Planner.Plan(new DateOnly(2025, 11, 1), DateTimeOffset.Parse("2025-12-08T15:04:00Z"), "America/Chicago"));
    }

    [Fact]
    public void Plan_UnknownTimeZoneId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Planner.Plan(new DateOnly(2026, 1, 10), DateTimeOffset.Parse("2025-12-08T15:04:00Z"), "Not/AZone"));
    }

    [Fact]
    public void Plan_CustomShortHorizonThreshold_ChangesClassification()
    {
        var customPlanner = new NextActionPlanner(new NextActionPlannerOptions(shortHorizonThresholdDays: 10));

        NextAction action = customPlanner.Plan(new DateOnly(2026, 1, 10), DateTimeOffset.Parse("2025-12-08T15:04:00Z"), "America/Chicago");

        Assert.Equal("follow_up_in_days", action.Type);
    }
}
