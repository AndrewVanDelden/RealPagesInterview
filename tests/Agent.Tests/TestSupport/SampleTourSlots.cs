namespace Agent.Tests.TestSupport;

// The two tour slots the planner offers an sms sent Tuesday 2025-12-09 at 09:00 in Chicago with no
// property calendar: Thursday and Friday at 10:00, and the text each is written as.
internal static class SampleTourSlots
{
    public static readonly IReadOnlyList<DateTimeOffset> Tuesday =
    [
        DateTimeOffset.Parse("2025-12-11T10:00:00-06:00"),
        DateTimeOffset.Parse("2025-12-12T10:00:00-06:00"),
    ];

    public static readonly IReadOnlyList<string> TuesdayText = ["Dec 11, 2025, 10:00 AM", "Dec 12, 2025, 10:00 AM"];
}
