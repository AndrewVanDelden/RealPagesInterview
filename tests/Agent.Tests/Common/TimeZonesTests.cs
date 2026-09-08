using Agent.Common;
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

    [Fact]
    public void ResolveOrUtc_KnownId_ReturnsTheZone()
    {
        Assert.Equal("America/Chicago", TimeZones.ResolveOrUtc("America/Chicago").Id);
    }

    [Fact]
    public void ResolveOrUtc_UnknownId_ReturnsUtc()
    {
        Assert.Equal(TimeZoneInfo.Utc, TimeZones.ResolveOrUtc("Not/AZone"));
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
}
