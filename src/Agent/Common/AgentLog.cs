using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agent.Common;

// JsonConverter<T> instances are stateless and shared across every deserialization call
// (JsonSerializerOptions.Converters is a static singleton - see AgentJsonOptions.Default),
// so there is no constructor-injection path into LenientExpectedOutcomeConverter. This
// AsyncLocal accessor is the one deliberate exception to constructor injection in this
// codebase, scoped narrowly to that single call site. AsyncLocal (not a plain static field)
// keeps concurrently-running callers - including parallel unit test runs - from seeing
// each other's configured factory: a value set here only flows to the async calls made
// from within the same logical call chain, not to unrelated calls running at the same time.
public static class AgentLog
{
    private static readonly AsyncLocal<ILoggerFactory?> CurrentFactory = new();

    public static IDisposable Configure(ILoggerFactory factory)
    {
        ILoggerFactory? previous = CurrentFactory.Value;
        CurrentFactory.Value = factory;
        return new RestoreOnDispose(previous);
    }

    public static ILogger CreateLogger(string categoryName) =>
        (CurrentFactory.Value ?? NullLoggerFactory.Instance).CreateLogger(categoryName);

    private sealed class RestoreOnDispose(ILoggerFactory? previous) : IDisposable
    {
        public void Dispose() => CurrentFactory.Value = previous;
    }
}
