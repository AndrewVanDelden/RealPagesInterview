using Agent.Common;
using Xunit;

namespace Agent.Tests.Common;

public class PresenceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAbsent_NullEmptyOrWhitespace_ReturnsTrue(string? value)
    {
        Assert.True(Presence.IsAbsent(value));
    }

    [Fact]
    public void IsAbsent_NonBlankValue_ReturnsFalse()
    {
        Assert.False(Presence.IsAbsent("Taylor"));
    }
}
