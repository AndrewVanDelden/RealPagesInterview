namespace Agent.Tests.TestSupport;

// A20 is stated over a zone's adjustment rules, not over the zone database that ships with
// the runtime: no real zone's transition covers 09:00 or 10:00, so the two branches it
// names are unreachable from any system zone. These two custom zones put a transition
// across 09:00 on purpose, so the rule has something to be proved against.
public static class SlotResolutionTestZones
{
    // Springs forward 08:30 to 09:30 on 15 March, so local 09:00 does not exist that day.
    public static TimeZoneInfo SpringForwardAcrossNineAm { get; } = CreateZone(
        "Test/SpringForwardAcrossNineAm",
        TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 8, 30, 0), month: 3, day: 15),
        TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), month: 11, day: 1));

    // Falls back 09:30 to 08:30 on 1 November, so local 09:00 is reached twice that day.
    public static TimeZoneInfo FallBackAcrossNineAm { get; } = CreateZone(
        "Test/FallBackAcrossNineAm",
        TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), month: 3, day: 15),
        TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 9, 30, 0), month: 11, day: 1));

    private static TimeZoneInfo CreateZone(string id, TimeZoneInfo.TransitionTime start, TimeZoneInfo.TransitionTime end)
    {
        TimeZoneInfo.AdjustmentRule rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            start,
            end);

        return TimeZoneInfo.CreateCustomTimeZone(
            id,
            TimeSpan.FromHours(-6),
            id,
            $"{id} Standard Time",
            $"{id} Daylight Time",
            [rule]);
    }
}
