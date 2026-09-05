using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agent.Common;

// The `X ?? NullX.Instance` null-object-logger idiom was copy-pasted (only the type
// parameter changing) across every class this project added logging to, plus AgentLog's
// own ILoggerFactory fallback. One place instead of five.
public static class LoggerDefaults
{
    public static ILogger<T> OrNullLogger<T>(this ILogger<T>? logger) => logger ?? NullLogger<T>.Instance;

    public static ILoggerFactory OrNullFactory(this ILoggerFactory? factory) => factory ?? NullLoggerFactory.Instance;
}
