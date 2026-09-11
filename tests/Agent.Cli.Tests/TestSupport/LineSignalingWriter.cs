namespace Agent.Cli.Tests.TestSupport;

// A StringWriter whose Seen task completes the first time a written line contains marker, so
// a test can tell when CliRunner wrote a particular stderr line relative to other work.
internal sealed class LineSignalingWriter(string marker) : StringWriter
{
    private readonly TaskCompletionSource seen = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Seen => seen.Task;

    public override void WriteLine(string? value)
    {
        base.WriteLine(value);
        if (value is not null && value.Contains(marker, StringComparison.Ordinal))
        {
            seen.TrySetResult();
        }
    }
}
