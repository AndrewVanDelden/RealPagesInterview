using Agent.Common;
using Xunit;

namespace Agent.Tests.Common;

public class ExceptionFormattingTests
{
    [Fact]
    public void ToDiagnosticString_ReturnsExceptionTypeNameAndMessage()
    {
        var exception = new InvalidOperationException("boom");

        string result = exception.ToDiagnosticString();

        Assert.Equal("InvalidOperationException: boom", result);
    }
}
