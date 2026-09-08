namespace Agent.Common;

// A6: an absent or unrecognized timezone id resolves as UTC. The ingest notes name the
// field, so the default is visible per record rather than silent.
public static class TimeZones
{
    public static bool TryResolve(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        if (timeZoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out TimeZoneInfo? found))
        {
            timeZone = found;
            return true;
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }

    public static TimeZoneInfo ResolveOrUtc(string? timeZoneId)
    {
        TryResolve(timeZoneId, out TimeZoneInfo timeZone);
        return timeZone;
    }

    // The calendar date an instant falls on in the record's timezone: the date horizons
    // are counted from (A7) and send days are floored to (A4).
    public static DateOnly ToLocalDate(DateTimeOffset instant, string? timeZoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, ResolveOrUtc(timeZoneId)).DateTime);

    // D21 and A20: the send slot (A5) is a local wall time, and a zone does not reach every
    // wall time exactly once. On the day a zone springs forward the slot may never happen; on
    // the day it falls back the slot happens twice. Stamping GetUtcOffset(wall time) on the
    // wall time, which is what the arithmetic reads like, emits an offset the zone never had
    // at the instant it names, and the offset travels with the value, so no reader downstream
    // can tell. Both branches are unreachable from the current zone database at 09:00 and
    // 10:00; the rule is stated over a zone's adjustment rules, and the tests build zones
    // whose transitions do cover the slot.
    public static ResolvedSlot ResolveSlot(TimeZoneInfo timeZone, DateTime localWallTime)
    {
        if (timeZone.IsInvalidTime(localWallTime))
        {
            // GetUtcOffset returns the offset from before the transition for a wall time
            // inside the gap, so this instant is already past the gap. Re-stamping it with
            // the offset the zone was on when it got there is what makes the value true.
            var pastTheGap = new DateTimeOffset(localWallTime, timeZone.GetUtcOffset(localWallTime));
            return new ResolvedSlot(pastTheGap.ToOffset(timeZone.GetUtcOffset(pastTheGap)), SlotResolution.ShiftedPastGap);
        }

        if (timeZone.IsAmbiguousTime(localWallTime))
        {
            // The same wall time under the larger offset is the earlier instant.
            TimeSpan earlier = timeZone.GetAmbiguousTimeOffsets(localWallTime).Max();
            return new ResolvedSlot(new DateTimeOffset(localWallTime, earlier), SlotResolution.EarlierOfTwo);
        }

        return new ResolvedSlot(new DateTimeOffset(localWallTime, timeZone.GetUtcOffset(localWallTime)), SlotResolution.Exact);
    }
}
