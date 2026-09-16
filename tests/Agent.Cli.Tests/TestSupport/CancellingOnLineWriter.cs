using System.Text;

namespace Agent.Cli.Tests.TestSupport;

// A StringWriter that cancels the run the first time a written line contains marker, so a test
// can cancel at an exact point in the batch: a line logged while an input line is being parsed
// cancels the run before the batch has decided whether to start that record. Log lines arrive as
// a StringBuilder and failure lines as a string, so both are watched.
internal sealed class CancellingOnLineWriter(string marker, CancellationTokenSource runCancellation) : StringWriter
{
    public override void WriteLine(string? value)
    {
        base.WriteLine(value);
        CancelIfMarked(value);
    }

    public override void WriteLine(StringBuilder? value)
    {
        base.WriteLine(value);
        CancelIfMarked(value?.ToString());
    }

    private void CancelIfMarked(string? line)
    {
        if (line is not null && line.Contains(marker, StringComparison.Ordinal))
        {
            runCancellation.Cancel();
        }
    }
}
