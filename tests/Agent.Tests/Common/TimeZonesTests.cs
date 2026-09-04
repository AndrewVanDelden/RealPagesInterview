using Agent.Common;
using Xunit;

namespace Agent.Tests.Common;

public class TimeZonesTests
{
    [Fact]
    public void Resolve_KnownIanaId_ReturnsTimeZoneInfo()
    {
        TimeZoneInfo timeZone = TimeZones.Resolve("America/Chicago");

        Assert.Equal("America/Chicago", timeZone.Id);
    }

    [Fact]
    public void Resolve_UnknownId_ThrowsArgumentException()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => TimeZones.Resolve("Not/AZone"));

        Assert.Contains("Not/AZone", exception.Message);
        Assert.Equal("timeZoneId", exception.ParamName);
    }
}
