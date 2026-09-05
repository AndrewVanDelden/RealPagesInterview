using Microsoft.Extensions.Logging;

namespace Agent.Tests.TestSupport;

// Routes every category to one pre-built logger, so a test can inject a CapturingLogger<T>
// wherever an ILoggerFactory (not an ILogger<T> directly) is what the production code needs -
// e.g. AgentLog.Configure, which only accepts a factory.
internal sealed class FakeLoggerFactory(ILogger logger) : ILoggerFactory
{
    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => logger;

    public void Dispose()
    {
    }
}
