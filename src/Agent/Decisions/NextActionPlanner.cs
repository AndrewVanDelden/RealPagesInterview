using Agent.Common;
using Agent.Domain;

namespace Agent.Decisions;

public sealed class NextActionPlanner(NextActionPlannerOptions? options = null) : INextActionPlanner
{
    private readonly NextActionPlannerOptions _options = options ?? new NextActionPlannerOptions();

    public NextAction Plan(DateOnly moveDateTarget, DateTimeOffset lastInteraction, string timeZoneId)
    {
        TimeZoneInfo timeZone = TimeZones.Resolve(timeZoneId);
        DateOnly lastInteractionDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(lastInteraction, timeZone).DateTime);

        int horizonDays = moveDateTarget.DayNumber - lastInteractionDate.DayNumber;

        if (horizonDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(moveDateTarget), moveDateTarget, "Move date target cannot precede the last interaction date.");
        }

        return horizonDays <= _options.ShortHorizonThresholdDays
            ? new NextAction("start_cadence", _options.ShortHorizonCadenceName, null)
            : new NextAction("follow_up_in_days", null, _options.LongHorizonFollowUpDays);
    }
}
