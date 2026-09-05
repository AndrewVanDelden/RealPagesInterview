using System.Text;
using Microsoft.Extensions.Logging;

namespace Agent.Cli.Logging;

// Shared plain-text log-line formatting for both FileLoggerProvider and
// ConsoleLoggerProvider - the two sinks render identically, so the format lives in one
// place instead of being copy-pasted across near-twin ILoggerProvider implementations.
internal static class LogLineFormatter
{
    public static StringBuilder Format(string categoryName, LogLevel logLevel, string message, Exception? exception, IExternalScopeProvider scopeProvider)
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

        return line;
    }
}
