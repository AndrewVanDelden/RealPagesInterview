using Agent.Domain;

namespace Agent.Decisions;

public sealed class NextActionPlanner(NextActionPlannerOptions? options = null) : INextActionPlanner
{
    private readonly NextActionPlannerOptions _options = options ?? new NextActionPlannerOptions();

    // A7: horizon is move_date_target minus the reference date (D10), already converted to
    // the record's local date by the caller. At most the threshold is short; an absent date
    // is long (no date, no cadence to start); a past date is short (the move is due).
    public NextAction Plan(DateOnly? moveDateTarget, DateOnly referenceDate)
    {
        if (moveDateTarget is null)
        {
            return LongHorizon();
        }

        int horizonDays = moveDateTarget.Value.DayNumber - referenceDate.DayNumber;

        return horizonDays <= _options.ShortHorizonThresholdDays
            ? new NextAction("start_cadence", _options.ShortHorizonCadenceName, null)
            : LongHorizon();
    }

    private NextAction LongHorizon() => new("follow_up_in_days", null, _options.LongHorizonFollowUpDays);
}
