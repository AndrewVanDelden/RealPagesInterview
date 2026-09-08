namespace Agent.Common;

// The "TaskId" scope key is an implicit contract across two files - CliRunner.cs is the
// one place that pushes it via BeginScope (D16: LeasingMessageAgent.cs and Evaluator.cs
// deliberately do not, to avoid a second nested scope) - and
// Agent.Cli.Logging.LogLineFormatter recognizes the Dictionary<string, object> shape to
// render it - with nothing at compile time tying the string literal together. One shared
// constant instead of independently-typed copies that could silently drift.
public static class LogKeys
{
    public const string TaskId = "TaskId";
}
