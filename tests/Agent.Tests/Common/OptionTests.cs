using Agent.Common;
using Xunit;

namespace Agent.Tests.Common;

public class OptionTests
{
    [Fact]
    public void Some_HasValueTrue_ValueSet()
    {
        Option<int> option = Option<int>.Some(7);

        Assert.True(option.HasValue);
        Assert.Equal(7, option.Value);
    }

    [Fact]
    public void None_HasValueFalse_ValueThrows()
    {
        Option<int> option = Option<int>.None();

        Assert.False(option.HasValue);
        Assert.Throws<InvalidOperationException>(() => option.Value);
    }

    [Fact]
    public void None_OfEnumType_ValueThrowsRatherThanReturningDefaultMember()
    {
        Option<DayOfWeek> option = Option<DayOfWeek>.None();

        Assert.Throws<InvalidOperationException>(() => option.Value);
    }
}
