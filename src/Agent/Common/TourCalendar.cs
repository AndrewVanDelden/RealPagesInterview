namespace Agent.Common;

// A property's tour calendar as its system of record states it: the weekdays it holds tours, the local
// time a tour starts, and the dates it is closed. Every member is optional, and a member the calendar
// does not state takes the tour slot planner's default.
public sealed record TourCalendar(
    IReadOnlyList<DayOfWeek>? OpenDays = null,
    TimeOnly? TourTime = null,
    IReadOnlyList<DateOnly>? ClosedDates = null);
