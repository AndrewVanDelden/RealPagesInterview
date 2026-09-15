using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Agent.Common;

// A6: an absent or unrecognized timezone id resolves as UTC. The ingest notes name the
// field, so the default is visible per record rather than silent.
public static partial class TimeZones
{
    // Each US state and DC with the IANA zone most of its population lives in, the fallback for a
    // record whose timezone id the runtime does not know but whose city_interest names a state.
    public static readonly FrozenDictionary<string, string> StateZoneIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "America/Chicago", ["AK"] = "America/Anchorage", ["AZ"] = "America/Phoenix", ["AR"] = "America/Chicago",
        ["CA"] = "America/Los_Angeles", ["CO"] = "America/Denver", ["CT"] = "America/New_York", ["DE"] = "America/New_York",
        ["DC"] = "America/New_York", ["FL"] = "America/New_York", ["GA"] = "America/New_York", ["HI"] = "Pacific/Honolulu",
        ["ID"] = "America/Boise", ["IL"] = "America/Chicago", ["IN"] = "America/Indiana/Indianapolis", ["IA"] = "America/Chicago",
        ["KS"] = "America/Chicago", ["KY"] = "America/New_York", ["LA"] = "America/Chicago", ["ME"] = "America/New_York",
        ["MD"] = "America/New_York", ["MA"] = "America/New_York", ["MI"] = "America/Detroit", ["MN"] = "America/Chicago",
        ["MS"] = "America/Chicago", ["MO"] = "America/Chicago", ["MT"] = "America/Denver", ["NE"] = "America/Chicago",
        ["NV"] = "America/Los_Angeles", ["NH"] = "America/New_York", ["NJ"] = "America/New_York", ["NM"] = "America/Denver",
        ["NY"] = "America/New_York", ["NC"] = "America/New_York", ["ND"] = "America/Chicago", ["OH"] = "America/New_York",
        ["OK"] = "America/Chicago", ["OR"] = "America/Los_Angeles", ["PA"] = "America/New_York", ["RI"] = "America/New_York",
        ["SC"] = "America/New_York", ["SD"] = "America/Chicago", ["TN"] = "America/Chicago", ["TX"] = "America/Chicago",
        ["UT"] = "America/Denver", ["VT"] = "America/New_York", ["VA"] = "America/New_York", ["WA"] = "America/Los_Angeles",
        ["WV"] = "America/New_York", ["WI"] = "America/Chicago", ["WY"] = "America/Denver",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // The zone id a record is scheduled in: its own when the runtime knows it; otherwise the zone of the
    // state its city_interest ends with ("Dallas, TX"), since synthetic_v2's unknown "America/Dallas"
    // with "Dallas, TX" is labeled Central time; otherwise its own id, which resolves as UTC downstream.
    // O(n) in the city_interest's length: one anchored match and one lookup.
    public static string? EffectiveZoneId(string? timeZoneId, string? cityInterest)
    {
        if (TryResolve(timeZoneId, out _))
        {
            return timeZoneId;
        }

        return cityInterest is not null
            && StateSuffix().Match(cityInterest) is { Success: true } state
            && StateZoneIds.TryGetValue(state.Groups[1].Value, out string? zoneId)
                ? zoneId
                : timeZoneId;
    }

    [GeneratedRegex(@",\s*([A-Za-z]{2})\s*$")]
    private static partial Regex StateSuffix();

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

    // A20: the send slot (A5) is a local wall time, and a zone does not reach every wall time
    // exactly once. On the day a zone springs forward the slot may never happen, and resolves
    // to the first instant at or after it, closest to the stated hour; on the day it falls
    // back the slot happens twice, and resolves to the earlier. Stamping GetUtcOffset(wall
    // time) on the wall time, the obvious arithmetic, emits an offset the zone never had at
    // that instant, and the offset travels with the value, so no reader downstream can tell.
    // Both branches are unreachable from the current zone database at 09:00 and 10:00; the
    // rule is stated over a zone's adjustment rules, and the tests build zones that reach it.
    public static ResolvedSlot ResolveSlot(TimeZoneInfo timeZone, DateTime localWallTime)
    {
        if (timeZone.IsInvalidTime(localWallTime))
        {
            return new ResolvedSlot(TransitionInstantAtOrAfter(timeZone, localWallTime), SlotResolution.ShiftedPastGap);
        }

        if (timeZone.IsAmbiguousTime(localWallTime))
        {
            // The same wall time under the larger offset is the earlier instant.
            TimeSpan earlier = timeZone.GetAmbiguousTimeOffsets(localWallTime).Max();
            return new ResolvedSlot(new DateTimeOffset(localWallTime, earlier), SlotResolution.EarlierOfTwo);
        }

        return new ResolvedSlot(new DateTimeOffset(localWallTime, timeZone.GetUtcOffset(localWallTime)), SlotResolution.Exact);
    }

    // The earliest instant at or after a gap's local wall time, found without assuming
    // the gap's width - a zone's rule tables are not public API, so this locates the boundary
    // by search rather than by reading the rule. GetUtcOffset on an invalid wall time returns
    // the offset from before the transition (documented behavior), so stamping that offset on
    // the wall time and re-reading the offset at the resulting instant gives a point already
    // past the gap: pre-offset and post-offset differ by exactly the gap's width, so that
    // point is never more than one gap-width past the boundary, which bounds the search.
    private static DateTimeOffset TransitionInstantAtOrAfter(TimeZoneInfo timeZone, DateTime localWallTime)
    {
        TimeSpan beforeOffset = timeZone.GetUtcOffset(localWallTime);
        var pastTheGap = new DateTimeOffset(localWallTime, beforeOffset);
        TimeSpan afterOffset = timeZone.GetUtcOffset(pastTheGap);

        DateTime invalid = localWallTime;
        DateTime valid = localWallTime + (afterOffset - beforeOffset);

        while (valid - invalid > TimeSpan.FromTicks(1))
        {
            DateTime midpoint = invalid + TimeSpan.FromTicks((valid - invalid).Ticks / 2);

            if (timeZone.IsInvalidTime(midpoint))
            {
                invalid = midpoint;
            }
            else
            {
                valid = midpoint;
            }
        }

        return new DateTimeOffset(valid, afterOffset);
    }
}
