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
}
