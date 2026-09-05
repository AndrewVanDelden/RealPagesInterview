using Microsoft.Extensions.Logging;

namespace Agent.Cli.Logging;

// A minimal file sink for Microsoft.Extensions.Logging. Lives in Agent.Cli, not Agent:
// wiring a physical log sink is a composition-root concern, the same reasoning that keeps
// TextWriter output/error as CliRunner constructor parameters rather than a library type.
// Implements ISupportExternalScope so a scope opened anywhere downstream (e.g.
// LeasingMessageAgent's per-record TaskId scope) renders here the same way the console
// provider renders it - LoggerFactory hands every ISupportExternalScope provider the one
// shared scope stack it manages, so a scope pushed via any logger is visible to all of them.
public sealed class FileLoggerProvider(string path) : ILoggerProvider, ISupportExternalScope
{
    private readonly StreamWriter writer = new(path, append: true) { AutoFlush = true };
    private readonly Lock sync = new();
    private IExternalScopeProvider scopeProvider = new LoggerExternalScopeProvider();

    public void SetScopeProvider(IExternalScopeProvider provider) => scopeProvider = provider;

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    // Takes the same lock the write path uses: without it, a write in flight on another
    // thread could race writer.Dispose() (see WriteEntry's lock below) and throw
    // ObjectDisposedException, or truncate the final line.
    public void Dispose()
    {
        lock (sync)
        {
            writer.Dispose();
        }
    }

    private void WriteEntry(string categoryName, LogLevel logLevel, string message, Exception? exception)
    {
        System.Text.StringBuilder line = LogLineFormatter.Format(categoryName, logLevel, message, exception, scopeProvider);

        lock (sync)
        {
            // Writes the StringBuilder directly (TextWriter.WriteLine(StringBuilder?), .NET 8+)
            // instead of line.ToString(), avoiding a throwaway string allocation per logged line.
            writer.WriteLine(line);
        }
    }

    private sealed class FileLogger(string categoryName, FileLoggerProvider provider) : ILogger
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
