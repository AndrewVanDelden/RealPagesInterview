using Microsoft.Extensions.Logging;

namespace Agent.Cli.Logging;

// Writes the same plain-text log lines as FileLoggerProvider, through the CLI's own
// injected `error` TextWriter rather than the real Console.Out/Console.Error.
// Microsoft.Extensions.Logging.Console's AddConsole is hardwired to the real console and
// cannot be redirected - that both pollutes production stdout (colliding with
// --eval-report's own console output on the same stream) and defeats every CliRunnerTests
// test's attempt to isolate itself from the real console via injected StringWriters. Does
// not own or dispose the writer: it belongs to whoever constructed CliRunner (Console.Error
// in production, a test's StringWriter in tests), not to this provider - the same reasoning
// FileLoggerProvider's own remarks cite for keeping TextWriter output/error as CliRunner
// constructor parameters rather than a library type, applied here to where logs go instead
// of just where plain-text output/error goes.
public sealed class ConsoleLoggerProvider(TextWriter writer) : ILoggerProvider, ISupportExternalScope
{
    private readonly Lock sync = new();
    private IExternalScopeProvider scopeProvider = new LoggerExternalScopeProvider();

    public void SetScopeProvider(IExternalScopeProvider provider) => scopeProvider = provider;

    public ILogger CreateLogger(string categoryName) => new ConsoleLogger(categoryName, this);

    public void Dispose()
    {
    }

    private void WriteEntry(string categoryName, LogLevel logLevel, string message, Exception? exception)
    {
        System.Text.StringBuilder line = LogLineFormatter.Format(categoryName, logLevel, message, exception, scopeProvider);

        lock (sync)
        {
            writer.WriteLine(line);
        }
    }

    private sealed class ConsoleLogger(string categoryName, ConsoleLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider.scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.WriteEntry(categoryName, logLevel, formatter(state, exception), exception);
        }
    }
}
