using Agent.Cli.Logging;
using Agent.Cli.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agent.Cli.Tests.Logging;

public class FileLoggerProviderTests
{
    private static string TempFilePath() => Path.Combine(Path.GetTempPath(), $"file-logger-provider-tests-{Guid.NewGuid():N}.log");

    [Fact]
    public void CreateLogger_LogInformation_WritesALineContainingCategoryAndMessage()
    {
        string path = TempFilePath();
        try
        {
            using (var provider = new FileLoggerProvider(path))
            {
                ILogger logger = provider.CreateLogger("MyCategory");
                logger.LogInformation("hello world");
            }

            string content = File.ReadAllText(path);
            Assert.Contains("MyCategory", content);
            Assert.Contains("hello world", content);
            Assert.Contains("Information", content);
        }
        finally
        {
            TestFiles.DeleteWithRetry(path);
        }
    }

    [Fact]
    public void CreateLogger_LogWithException_AppendsExceptionDetailToTheLine()
    {
        string path = TempFilePath();
        try
        {
            using (var provider = new FileLoggerProvider(path))
            {
                ILogger logger = provider.CreateLogger("MyCategory");
                logger.LogError(new InvalidOperationException("boom"), "something failed");
            }

            string content = File.ReadAllText(path);
            Assert.Contains("something failed", content);
            Assert.Contains("InvalidOperationException", content);
            Assert.Contains("boom", content);
        }
        finally
        {
            TestFiles.DeleteWithRetry(path);
        }
    }

    [Fact]
    public void CreateLogger_LogLevelNone_IsDisabledAndWritesNothing()
    {
        string path = TempFilePath();
        try
        {
            using (var provider = new FileLoggerProvider(path))
            {
                ILogger logger = provider.CreateLogger("MyCategory");

                Assert.False(logger.IsEnabled(LogLevel.None));
                logger.Log(LogLevel.None, new EventId(0), "state", null, (state, ex) => state);
            }

            Assert.Equal(string.Empty, File.ReadAllText(path));
        }
        finally
        {
            TestFiles.DeleteWithRetry(path);
        }
    }

    [Fact]
    public void CreateLogger_LogWithinScope_LineIncludesScopeState()
    {
        string path = TempFilePath();
        try
        {
            using (var provider = new FileLoggerProvider(path))
            {
                provider.SetScopeProvider(new LoggerExternalScopeProvider());
                ILogger logger = provider.CreateLogger("MyCategory");

                using (logger.BeginScope(new Dictionary<string, object> { ["TaskId"] = "abc-123" }))
                {
                    logger.LogInformation("processing");
                }
            }

            string content = File.ReadAllText(path);
            Assert.Contains("abc-123", content);
        }
        finally
        {
            TestFiles.DeleteWithRetry(path);
        }
    }

    [Fact]
    public void SetScopeProvider_NotCalled_StillWritesUsingTheDefaultScopeProvider()
    {
        string path = TempFilePath();
        try
        {
            using (var provider = new FileLoggerProvider(path))
            {
                ILogger logger = provider.CreateLogger("MyCategory");
                logger.LogInformation("no explicit scope provider set");
            }

            Assert.Contains("no explicit scope provider set", File.ReadAllText(path));
        }
        finally
        {
            TestFiles.DeleteWithRetry(path);
        }
    }

    [Fact]
    public void MultipleLoggersFromSameProvider_WriteConcurrentlyWithoutCorruptingTheFile()
    {
        string path = TempFilePath();
        try
        {
            using (var provider = new FileLoggerProvider(path))
            {
                ILogger logger = provider.CreateLogger("MyCategory");
                Parallel.For(0, 50, i => logger.LogInformation("line {Index}", i));
            }

            string[] lines = File.ReadAllLines(path);
            Assert.Equal(50, lines.Length);
        }
        finally
        {
            TestFiles.DeleteWithRetry(path);
        }
    }

    // LoggerFactory.Create's own DI container does not reliably dispose an ILoggerProvider
    // instance that was handed to it pre-constructed (it didn't create the instance, so it
    // doesn't assume ownership of it) - CliRunner therefore disposes the provider itself,
    // explicitly, rather than trusting the factory's Dispose to cascade to it. This test
    // follows that same ownership pattern.
    [Fact]
    public void LoggerFactory_WithFileLoggerProvider_WiresScopeProviderAutomatically()
    {
        string path = TempFilePath();
        try
        {
            using (var provider = new FileLoggerProvider(path))
            using (ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddProvider(provider)))
            {
                ILogger<FileLoggerProviderTests> logger = factory.CreateLogger<FileLoggerProviderTests>();
                using (logger.BeginScope(new Dictionary<string, object> { ["TaskId"] = "via-factory" }))
                {
                    logger.LogInformation("processed through the factory");
                }
            }

            string content = File.ReadAllText(path);
            Assert.Contains("via-factory", content);
        }
        finally
        {
            TestFiles.DeleteWithRetry(path);
        }
    }
}
