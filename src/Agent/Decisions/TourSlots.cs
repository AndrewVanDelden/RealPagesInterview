using System.Collections.Frozen;
using Agent.Common;

namespace Agent.Decisions;

// The tour slots an invitation offers are a decision, not prose: the next two open days counted from
// the message's local send date, starting two days after it, since tours booked within three days of
// an inquiry show far more often than later ones and the fitted example offers the days two and three
// days out. A day is open when the calendar names its weekday and does not close its date. With no
// calendar the open days are Monday to Saturday, because Saturday leasing hours are common and Sunday
// hours are not, and a Sunday slot the office does not staff is a promise the property breaks. Each
// slot is at the calendar's tour time, 10:00 with none, as a wall time the record's zone resolves.
public static class TourSlots
{
    public const int LeadDays = 2;

    public const int SlotCount = 2;

    // A calendar whose open days never occur, or are all closed, would otherwise search forever; a
    // year is longer than any tour invitation could reasonably look ahead.
    private const int SearchDays = 366;

    private static readonly FrozenSet<DayOfWeek> DefaultOpenDays = new[]
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
    }.ToFrozenSet();

    private static readonly TimeOnly DefaultTourTime = new(10, 0);

    // O(d + c) time and O(c) space: d days searched, at most SearchDays, and c closed dates indexed once.
    public static IReadOnlyList<DateTimeOffset> For(DateTimeOffset sendAt, string? timeZoneId, TourCalendar? calendar)
    {
        TimeZoneInfo timeZone = TimeZones.ResolveOrUtc(timeZoneId);
        DateOnly sendDate = TimeZones.ToLocalDate(sendAt, timeZoneId);
        IReadOnlySet<DayOfWeek> openDays = calendar?.OpenDays is { } days ? days.ToHashSet() : DefaultOpenDays;
        TimeOnly tourTime = calendar?.TourTime ?? DefaultTourTime;
        HashSet<DateOnly> closedDates = calendar?.ClosedDates is { } closed ? [.. closed] : [];
        DateOnly lastDay = sendDate.AddDays(LeadDays + SearchDays);
        var slots = new List<DateTimeOffset>(SlotCount);

        for (DateOnly day = sendDate.AddDays(LeadDays); slots.Count < SlotCount && day <= lastDay; day = day.AddDays(1))
        {
            if (openDays.Contains(day.DayOfWeek) && !closedDates.Contains(day))
            {
                slots.Add(TimeZones.ResolveSlot(timeZone, day.ToDateTime(tourTime)).Instant);
            }
        }

        return slots;
    }
}
