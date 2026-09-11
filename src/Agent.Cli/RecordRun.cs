using Agent.Domain;
using Agent.Ingest;
using Agent.Orchestration;

namespace Agent.Cli;

// What one record's run leaves for the batch's fold, which takes runs in input order as soon as
// every earlier record has finished. Records run concurrently and finish in any order, so nothing
// a record produces is written anywhere shared while it runs: it comes back here, and the fold
// alone writes the rows, counts the failure and writes the stderr line.
internal abstract record RecordRun(ProspectCase Case)
{
    public sealed record Completed(ProspectCase Case, IngestNotes IngestNotes, AgentRunResult Result, double LatencyMs) : RecordRun(Case);

    // Per-record isolation: every input shape has a default (D1), so only a bug throws, and it
    // costs its own record and nothing else.
    public sealed record Failed(ProspectCase Case, Exception Exception) : RecordRun(Case);
}
