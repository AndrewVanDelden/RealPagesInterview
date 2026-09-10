using Agent.Common;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Common;

// A6: an absent or unrecognized timezone resolves as UTC; the ingest notes name it.
public class TimeZonesTests
{
    [Fact]
    public void TryResolve_KnownIanaId_ReturnsTrueAndTheZone()
    {
        bool recognized = TimeZones.TryResolve("America/Chicago", out TimeZoneInfo timeZone);

        Assert.True(recognized);
        Assert.Equal("America/Chicago", timeZone.Id);
    }

    [Fact]
    public void TryResolve_UnknownId_ReturnsFalseAndUtc()
    {
        bool recognized = TimeZones.TryResolve("Not/AZone", out TimeZoneInfo timeZone);

        Assert.False(recognized);
        Assert.Equal(TimeZoneInfo.Utc, timeZone);
    }

    [Fact]
    public void TryResolve_Null_ReturnsFalseAndUtc()
    {
        bool recognized = TimeZones.TryResolve(null, out TimeZoneInfo timeZone);

        Assert.False(recognized);
        Assert.Equal(TimeZoneInfo.Utc, timeZone);
    }

    // 2025-12-25T02:30:00Z is 2025-12-24 18:30 in America/Los_Angeles (UTC-8 in December).
    // The local date, not the UTC date, is the one horizons are counted from.
    [Fact]
    public void ToLocalDate_InstantAfterMidnightUtc_UsesTheZoneLocalDate()
    {
        DateOnly localDate = TimeZones.ToLocalDate(DateTimeOffset.Parse("2025-12-25T02:30:00Z"), "America/Los_Angeles");

        Assert.Equal(new DateOnly(2025, 12, 24), localDate);
    }

    [Fact]
    public void ToLocalDate_UnknownZone_UsesTheUtcDate()
    {
        DateOnly localDate = TimeZones.ToLocalDate(DateTimeOffset.Parse("2025-12-25T02:30:00Z"), "Not/AZone");

        Assert.Equal(new DateOnly(2025, 12, 25), localDate);
    }

    // D21 and A20: a wall time the zone reaches exactly once resolves to that instant with the
    // offset the zone had; a wall time inside a spring-forward gap resolves past the gap and
    // never carries an offset the zone did not have at that instant; a wall time the zone
    // reaches twice resolves to the earlier of the two.
    [Fact]
    public void ResolveSlot_WallTimeTheZoneReachesOnce_IsExactAndCarriesTheZoneOffset()
    {
        var slot = new DateTime(2026, 6, 1, 9, 0, 0);

        ResolvedSlot resolved = TimeZones.ResolveSlot(SlotResolutionTestZones.SpringForwardAcrossNineAm, slot);

        Assert.Equal(SlotResolution.Exact, resolved.Resolution);
        Assert.Equal(new DateTimeOffset(slot, TimeSpan.FromHours(-5)), resolved.Instant);
    }

    // PR #20 review (D21 addendum): the slot must resolve to the transition instant itself
    // (09:30, the first valid instant), not to the wall time shifted by the gap's width
    // (10:00) - the shift overshoots by however far into the gap the slot fell, which is
    // farther from A5's requested hour than the transition instant is.
    [Fact]
    public void ResolveSlot_WallTimeInsideASpringForwardGap_ResolvesToTheTransitionInstant()
    {
        var slot = new DateTime(2026, 3, 15, 9, 0, 0);

        ResolvedSlot resolved = TimeZones.ResolveSlot(SlotResolutionTestZones.SpringForwardAcrossNineAm, slot);

        Assert.Equal(SlotResolution.ShiftedPastGap, resolved.Resolution);
        Assert.Equal(new DateTimeOffset(2026, 3, 15, 9, 30, 0, TimeSpan.FromHours(-5)), resolved.Instant);
    }

    // A slot requested right at the gap's own start is the case where the transition instant
    // and "shift by the gap width" agree (both land exactly on the gap's far edge); this is
    // the boundary the two candidate rules cannot be told apart by, so it is covered on its
    // own rather than only through the property sweep below.
    [Fact]
    public void ResolveSlot_WallTimeAtTheStartOfAGap_ResolvesToTheTransitionInstant()
    {
        var slot = new DateTime(2026, 3, 15, 8, 30, 0);

        ResolvedSlot resolved = TimeZones.ResolveSlot(SlotResolutionTestZones.SpringForwardAcrossNineAm, slot);

        Assert.Equal(SlotResolution.ShiftedPastGap, resolved.Resolution);
        Assert.Equal(new DateTimeOffset(2026, 3, 15, 9, 30, 0, TimeSpan.FromHours(-5)), resolved.Instant);
    }

    [Fact]
    public void ResolveSlot_WallTimeTheZoneReachesTwice_IsTheEarlierInstant()
    {
        var slot = new DateTime(2026, 11, 1, 9, 0, 0);

        ResolvedSlot resolved = TimeZones.ResolveSlot(SlotResolutionTestZones.FallBackAcrossNineAm, slot);

        Assert.Equal(SlotResolution.EarlierOfTwo, resolved.Resolution);
        Assert.Equal(new DateTimeOffset(slot, TimeSpan.FromHours(-5)), resolved.Instant);
        Assert.True(resolved.Instant < new DateTimeOffset(slot, TimeSpan.FromHours(-6)));
    }

    // Step 45, the property the two branches above exist to protect: whatever the zone and
    // whatever the wall time, the offset stamped on the result is the offset that zone was
    // actually on at that instant. A value carrying any other offset names an instant the zone
    // never had, and the offset travels with the value, so nothing downstream can detect it.
    [Fact]
    public void ResolveSlot_EverySystemZoneAcrossItsTransitions_StampsTheOffsetTheZoneWasOn()
    {
        AssertOverEverySystemZoneTransition(
            "stamped an offset the zone was not on",
            (timeZone, _, resolved) => timeZone.GetUtcOffset(resolved.Instant) == resolved.Instant.Offset);
    }

    // The same sweep, and the property A4 depends on: the resolved instant never lands before
    // the wall time it was asked for, so a slot resolved on a transition day cannot fall back
    // behind the floor that put the send on that day.
    [Fact]
    public void ResolveSlot_EverySystemZoneAcrossItsTransitions_LandsAtOrAfterTheWallTime()
    {
        AssertOverEverySystemZoneTransition(
            "resolved to a local time before the slot",
            (timeZone, wallTime, resolved) => TimeZoneInfo.ConvertTime(resolved.Instant, timeZone).DateTime >= wallTime);
    }

    // Every system zone, at both send hours A5 names, on every day its 2026 rules change the
    // offset and the day either side: the days a transition can make a wall time vanish or
    // repeat. One test rather than one case per zone, because the count of system zones is a
    // property of the runtime and would otherwise move the suite's test count from machine to
    // machine. Every failure is collected, so one run names every zone that broke the rule.
    // The slots are swept once, in TransitionSlots below, and shared by both [Fact]s that call
    // this: TransitionDaysIn's output depends only on the zone and the year, not on which
    // property is being checked, so sweeping it separately per [Fact] would pay the same
    // ~140-zone, 365-day scan twice for no difference in coverage (PR #20 review).
    private static void AssertOverEverySystemZoneTransition(string failure, Func<TimeZoneInfo, DateTime, ResolvedSlot, bool> holds)
    {
        List<string> failures = [];

        foreach ((TimeZoneInfo timeZone, DateTime wallTime) in TransitionSlots.Value)
        {
            ResolvedSlot resolved = TimeZones.ResolveSlot(timeZone, wallTime);

            if (!holds(timeZone, wallTime, resolved))
            {
                failures.Add($"{timeZone.Id} at {wallTime:O} {failure}: {resolved.Instant:O} ({resolved.Resolution})");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.NotEmpty(TransitionSlots.Value);
    }

    private static readonly int[] SendHours = [9, 10];

    // Lazy, not eager: built on first use by whichever [Fact] runs first, then reused by the
    // other, rather than computed for every test class instantiation whether or not a
    // transition-sweep test runs.
    private static readonly Lazy<IReadOnlyList<(TimeZoneInfo TimeZone, DateTime WallTime)>> TransitionSlots = new(BuildTransitionSlots);

    private static List<(TimeZoneInfo TimeZone, DateTime WallTime)> BuildTransitionSlots()
    {
        List<(TimeZoneInfo, DateTime)> slots = [];

        foreach (TimeZoneInfo timeZone in TimeZoneInfo.GetSystemTimeZones())
        {
            foreach (DateOnly day in TransitionDaysIn(timeZone, year: 2026))
            {
                foreach (int hour in SendHours)
                {
                    slots.Add((timeZone, day.ToDateTime(new TimeOnly(hour, 0))));
                }
            }
        }

        return slots;
    }

    // The days a zone's offset changes in the given year, plus the day before each, found by
    // walking the year a day at a time: a transition inside a day shows up as a change between
    // two day boundaries, and the wall time that vanishes or repeats is on one of the two.
    // O(days in a year) per zone over the zones the runtime knows, bounded by the zone
    // database rather than by any input.
    private static IEnumerable<DateOnly> TransitionDaysIn(TimeZoneInfo timeZone, int year)
    {
        if (!timeZone.SupportsDaylightSavingTime)
        {
            yield break;
        }

        var start = new DateTimeOffset(new DateTime(year, 1, 1), TimeSpan.Zero);
        TimeSpan previousOffset = timeZone.GetUtcOffset(start);

        for (DateTimeOffset instant = start.AddDays(1); instant.Year == year; instant = instant.AddDays(1))
        {
            TimeSpan offset = timeZone.GetUtcOffset(instant);

            if (offset == previousOffset)
            {
                continue;
            }

            previousOffset = offset;
            DateOnly changedOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);

            yield return changedOn.AddDays(-1);
            yield return changedOn;
        }
    }
}
