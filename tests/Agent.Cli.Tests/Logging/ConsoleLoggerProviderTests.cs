using Agent.Cli.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agent.Cli.Tests.Logging;

public class ConsoleLoggerProviderTests
{
    [Fact]
    public void CreateLogger_LogInformation_WritesALineContainingCategoryAndMessage()
    {
        var writer = new StringWriter();
        using (var provider = new ConsoleLoggerProvider(writer))
        {
            ILogger logger = provider.CreateLogger("MyCategory");
            logger.LogInformation("hello world");
        }

        string content = writer.ToString();
        Assert.Contains("MyCategory", content);
        Assert.Contains("hello world", content);
        Assert.Contains("Information", content);
    }

    [Fact]
    public void CreateLogger_LogWithException_AppendsExceptionDetailToTheLine()
    {
        var writer = new StringWriter();
        using (var provider = new ConsoleLoggerProvider(writer))
        {
            ILogger logger = provider.CreateLogger("MyCategory");
            logger.LogError(new InvalidOperationException("boom"), "something failed");
        }

        string content = writer.ToString();
        Assert.Contains("something failed", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("boom", content);
    }

    [Fact]
    public void CreateLogger_LogLevelNone_IsDisabledAndWritesNothing()
    {
        var writer = new StringWriter();
        using (var provider = new ConsoleLoggerProvider(writer))
        {
            ILogger logger = provider.CreateLogger("MyCategory");

            Assert.False(logger.IsEnabled(LogLevel.None));
            logger.Log(LogLevel.None, new EventId(0), "state", null, (state, ex) => state);
        }

        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void CreateLogger_LogWithinScope_LineIncludesScopeState()
    {
        var writer = new StringWriter();
        using (var provider = new ConsoleLoggerProvider(writer))
        {
            provider.SetScopeProvider(new LoggerExternalScopeProvider());
            ILogger logger = provider.CreateLogger("MyCategory");

            using (logger.BeginScope(new Dictionary<string, object> { ["TaskId"] = "abc-123" }))
            {
                logger.LogInformation("processing");
            }
        }

        Assert.Contains("abc-123", writer.ToString());
    }

    // The whole point of this provider: it must never dispose the writer it was given,
    // since that writer belongs to whoever constructed CliRunner (Console.Error in
    // production, a StringWriter in tests) - disposing it would be reaching outside this
    // provider's own ownership boundary.
    [Fact]
    public void Dispose_DoesNotDisposeTheUnderlyingWriter()
    {
        var writer = new StringWriter();
        var provider = new ConsoleLoggerProvider(writer);
        ILogger logger = provider.CreateLogger("MyCategory");
        logger.LogInformation("before dispose");

        provider.Dispose();

        writer.WriteLine("still usable after provider disposal");
        Assert.Contains("still usable after provider disposal", writer.ToString());
    }

    [Fact]
    public void LoggerFactory_WithConsoleLoggerProvider_WiresScopeProviderAutomatically()
    {
        var writer = new StringWriter();
        using (var provider = new ConsoleLoggerProvider(writer))
        using (ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddProvider(provider)))
        {
            ILogger<ConsoleLoggerProviderTests> logger = factory.CreateLogger<ConsoleLoggerProviderTests>();
            using (logger.BeginScope(new Dictionary<string, object> { ["TaskId"] = "via-factory" }))
            {
                logger.LogInformation("processed through the factory");
            }
        }

        Assert.Contains("via-factory", writer.ToString());
    }
}
