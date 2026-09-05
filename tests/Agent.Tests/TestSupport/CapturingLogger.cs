using Microsoft.Extensions.Logging;

namespace Agent.Tests.TestSupport;

// A minimal capturing ILogger<T> fake for asserting level/message/exception without
// standing up a real ILoggerFactory or provider in every test that needs one.
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = [];

    public List<object> Scopes { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        Scopes.Add(state);
        return NoopScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));

    internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }
}
