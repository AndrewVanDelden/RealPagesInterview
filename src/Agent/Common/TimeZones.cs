namespace Agent.Common;

public static class TimeZones
{
    public static TimeZoneInfo Resolve(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException($"'{timeZoneId}' is not a recognized time zone id.", nameof(timeZoneId), ex);
        }
    }
}
