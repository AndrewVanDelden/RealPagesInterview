using Agent.Common;
using Xunit;

namespace Agent.Tests.Common;

public class ResultTests
{
    [Fact]
    public void Success_IsSuccessTrue_ValueSet_ErrorThrows()
    {
        Result<int> result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_IsSuccessFalse_ValueThrows_ErrorSet()
    {
        Result<int> result = Result<int>.Failure("bad input");

        Assert.False(result.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Equal("bad input", result.Error);
    }
}
