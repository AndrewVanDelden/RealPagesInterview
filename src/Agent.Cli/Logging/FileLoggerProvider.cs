using System.Text;
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

    public void Dispose() => writer.Dispose();

    private void WriteEntry(string categoryName, LogLevel logLevel, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(DateTimeOffset.UtcNow.ToString("O"))
            .Append(" [").Append(logLevel).Append("] ")
            .Append(categoryName)
            .Append(": ").Append(message);

        // A scope built from a key/value collection (BeginScope(new Dictionary<string, object>{...}),
        // the shape LeasingMessageAgent and Evaluator both push a TaskId through) renders its
        // pairs directly - the default ToString() on a Dictionary is just its type name, which
        // would make the one thing this scope exists for (surfacing the TaskId in the log line)
        // invisible.
        scopeProvider.ForEachScope(
            (scope, sb) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                {
                    foreach (KeyValuePair<string, object> pair in pairs)
                    {
                        sb.Append(' ').Append(pair.Key).Append('=').Append(pair.Value);
                    }
                }
                else
                {
                    sb.Append(" => ").Append(scope);
                }
            },
            line);

        if (exception is not null)
        {
            line.Append(Environment.NewLine).Append(exception);
        }

        lock (sync)
        {
            writer.WriteLine(line.ToString());
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
