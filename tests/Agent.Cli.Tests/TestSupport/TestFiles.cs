namespace Agent.Cli.Tests.TestSupport;

// FileLoggerProvider's AutoFlush writer means a log-file test can leave the file briefly
// held by an antivirus/indexer scan the instant it's closed - a well-known Windows flake for
// rapidly written-then-deleted files, not a defect in FileLoggerProvider's own (synchronous,
// correct) Dispose. A short bounded retry absorbs that without masking a real leak: a
// genuinely un-disposed handle would still fail after every retry.
internal static class TestFiles
{
    public static void DeleteWithRetry(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
        }
    }
}
