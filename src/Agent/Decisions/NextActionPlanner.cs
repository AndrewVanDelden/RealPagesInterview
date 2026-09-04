using Agent.Domain;

namespace Agent.Decisions;

public sealed class NextActionPlanner(NextActionPlannerOptions? options = null) : INextActionPlanner
{
    private readonly NextActionPlannerOptions _options = options ?? new NextActionPlannerOptions();

    public NextAction Plan(DateOnly moveDateTarget, DateOnly lastInteractionDate)
    {
        int horizonDays = moveDateTarget.DayNumber - lastInteractionDate.DayNumber;

        return horizonDays <= _options.ShortHorizonThresholdDays
            ? new NextAction("start_cadence", _options.ShortHorizonCadenceName, null)
            : new NextAction("follow_up_in_days", null, _options.LongHorizonFollowUpDays);
    }
}
