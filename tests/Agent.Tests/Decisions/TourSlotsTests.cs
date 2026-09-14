using Agent.Common;
using Agent.Decisions;
using Xunit;

namespace Agent.Tests.Decisions;

// A tour invitation offers the next two open tour slots, counted from the message's local send date:
// the first slot is at least two days after that date, so a tour lands inside the three days after an
// inquiry in which tours show best. A day is open when the property's calendar names its weekday and
// does not close its date; with no calendar the open days are Monday to Saturday, since Sunday leasing
// hours are not universal. Each slot is at the calendar's tour time, and 10:00 with none, in the
// record's zone.
public class TourSlotsTests
{
    private const string Chicago = "America/Chicago";

    [Theory]
    // Tuesday: the hold-out's send day, two and three days out.
    [InlineData("2025-12-09T09:00:00-06:00", "2025-12-11T10:00:00-06:00", "2025-12-12T10:00:00-06:00")]
    // Sunday: the blind set's send day.
    [InlineData("2026-10-25T09:00:00-05:00", "2026-10-27T10:00:00-05:00", "2026-10-28T10:00:00-05:00")]
    // Thursday: Saturday is open and Sunday is skipped.
    [InlineData("2025-12-11T09:00:00-06:00", "2025-12-13T10:00:00-06:00", "2025-12-15T10:00:00-06:00")]
    // Friday: two days out is Sunday, so the first open day is Monday.
    [InlineData("2025-12-12T09:00:00-06:00", "2025-12-15T10:00:00-06:00", "2025-12-16T10:00:00-06:00")]
    public void For_NoCalendar_OffersTheNextTwoOpenDaysFromTwoDaysOutAtTen(string sendAt, string first, string second)
    {
        IReadOnlyList<DateTimeOffset> slots = TourSlots.For(DateTimeOffset.Parse(sendAt), Chicago, calendar: null);

        Assert.Equal([DateTimeOffset.Parse(first), DateTimeOffset.Parse(second)], slots);
        Assert.All(slots, slot => Assert.Equal(DateTimeOffset.Parse(first).Offset, slot.Offset));
    }

    // The days are counted from the local send date, not the instant's UTC date: 02:00 UTC on a
    // Sunday is still Saturday evening in Chicago.
    [Fact]
    public void For_SendInstantOnALaterUtcDate_CountsFromTheLocalDate()
    {
        IReadOnlyList<DateTimeOffset> slots = TourSlots.For(DateTimeOffset.Parse("2026-10-25T02:00:00Z"), Chicago, calendar: null);

        Assert.Equal([DateTimeOffset.Parse("2026-10-26T10:00:00-05:00"), DateTimeOffset.Parse("2026-10-27T10:00:00-05:00")], slots);
    }

    // The property's calendar wins: its weekdays, its tour time, and a closed date skipped.
    [Fact]
    public void For_Calendar_UsesItsDaysItsTimeAndSkipsAClosedDate()
    {
        var calendar = new TourCalendar(
            OpenDays: [DayOfWeek.Tuesday, DayOfWeek.Thursday],
            TourTime: new TimeOnly(14, 0),
            ClosedDates: [new DateOnly(2025, 12, 11)]);

        IReadOnlyList<DateTimeOffset> slots = TourSlots.For(DateTimeOffset.Parse("2025-12-09T09:00:00-06:00"), Chicago, calendar);

        Assert.Equal([DateTimeOffset.Parse("2025-12-16T14:00:00-06:00"), DateTimeOffset.Parse("2025-12-18T14:00:00-06:00")], slots);
    }

    // A calendar that states only a time keeps the default days, and one that states only days keeps
    // the default time.
    [Fact]
    public void For_CalendarStatingOnlySomeMembers_KeepsTheDefaultsForTheRest()
    {
        IReadOnlyList<DateTimeOffset> timeOnly = TourSlots.For(
            DateTimeOffset.Parse("2025-12-09T09:00:00-06:00"), Chicago, new TourCalendar(TourTime: new TimeOnly(15, 30)));
        IReadOnlyList<DateTimeOffset> daysOnly = TourSlots.For(
            DateTimeOffset.Parse("2025-12-09T09:00:00-06:00"), Chicago, new TourCalendar(OpenDays: [DayOfWeek.Sunday]));

        Assert.Equal([DateTimeOffset.Parse("2025-12-11T15:30:00-06:00"), DateTimeOffset.Parse("2025-12-12T15:30:00-06:00")], timeOnly);
        Assert.Equal([DateTimeOffset.Parse("2025-12-14T10:00:00-06:00"), DateTimeOffset.Parse("2025-12-21T10:00:00-06:00")], daysOnly);
    }

    // An unknown zone is UTC, the rule every other date in the program follows.
    [Fact]
    public void For_UnknownZone_ResolvesTheSlotsInUtc()
    {
        IReadOnlyList<DateTimeOffset> slots = TourSlots.For(DateTimeOffset.Parse("2026-10-25T09:00:00Z"), "Not/AZone", calendar: null);

        Assert.Equal([DateTimeOffset.Parse("2026-10-27T10:00:00Z"), DateTimeOffset.Parse("2026-10-28T10:00:00Z")], slots);
    }

    // A calendar with no open day, or one whose every open day in the search window is closed, has
    // no slot to offer, so none is invented.
    [Fact]
    public void For_CalendarWithNoOpenDay_OffersNoSlot()
    {
        IReadOnlyList<DateTimeOffset> slots = TourSlots.For(
            DateTimeOffset.Parse("2025-12-09T09:00:00-06:00"), Chicago, new TourCalendar(OpenDays: []));

        Assert.Empty(slots);
    }
}
