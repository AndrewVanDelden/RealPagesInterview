namespace Agent.Common;

// The "TaskId" scope key is an implicit contract across three files - CliRunner.cs,
// LeasingMessageAgent.cs, and Evaluator.cs push it via BeginScope, and
// Agent.Cli.Logging.LogLineFormatter recognizes the Dictionary<string, object> shape to
// render it - with nothing at compile time tying the string literal together. One shared
// constant instead of three independently-typed copies that could silently drift.
public static class LogKeys
{
    public const string TaskId = "TaskId";
}
