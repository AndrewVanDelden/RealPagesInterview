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
    public void None_HasValueFalse_ValueDefault()
    {
        Option<int> option = Option<int>.None();

        Assert.False(option.HasValue);
        Assert.Equal(default, option.Value);
    }
}
