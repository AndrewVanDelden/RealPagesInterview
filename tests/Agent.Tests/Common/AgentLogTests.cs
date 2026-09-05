using Agent.Common;
using Agent.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Agent.Tests.Common;

public class AgentLogTests
{
    [Fact]
    public void CreateLogger_NotConfigured_ReturnsUsableNullLogger()
    {
        ILogger logger = AgentLog.CreateLogger("unconfigured-category");

        // NullLogger's IsEnabled always returns false - the observable proof this is
        // the silent fallback and not a real, wired-up factory.
        Assert.False(logger.IsEnabled(LogLevel.Critical));
    }

    [Fact]
    public void Configure_ThenCreateLogger_RoutesThroughTheConfiguredFactory()
    {
        var capturing = new CapturingLogger<AgentLogTests>();
        using ILoggerFactory factory = new FakeLoggerFactory(capturing);

        using (AgentLog.Configure(factory))
        {
            ILogger logger = AgentLog.CreateLogger("configured-category");
            logger.LogWarning("test warning");
        }

        Assert.Single(capturing.Entries);
        Assert.Equal(LogLevel.Warning, capturing.Entries[0].Level);
    }

    [Fact]
    public void Configure_Dispose_RestoresThePreviouslyConfiguredFactory()
    {
        var outer = new CapturingLogger<AgentLogTests>();
        var inner = new CapturingLogger<AgentLogTests>();

        using (AgentLog.Configure(new FakeLoggerFactory(outer)))
        {
            using (AgentLog.Configure(new FakeLoggerFactory(inner)))
            {
                AgentLog.CreateLogger("nested").LogInformation("inner");
            }

            AgentLog.CreateLogger("nested").LogInformation("outer-again");
        }

        Assert.Single(inner.Entries);
        Assert.Single(outer.Entries);
    }
}
