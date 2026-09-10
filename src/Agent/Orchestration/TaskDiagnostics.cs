using Agent.Ingest;

namespace Agent.Orchestration;

// D61: LatencyMs is the wall-clock elapsed of exactly one LeasingMessageAgent.RunAsync call,
// measured by the batch loop that made it and handed from one variable to this row and to
// ScoredRun.LatencyMs, so the diagnostics file and the eval report can never state two
// latencies for one record. It excludes reading and parsing the input line, IngestNotes.Describe,
// every output write and the evaluator. Non-nullable: a record whose RunAsync threw gets no
// diagnostics row at all, so there is no row here without a measurement behind it. The batch's
// own elapsed is not here (this file is one row per unit of work); it is on the scorecard and
// the Batch complete log line.
public sealed record TaskDiagnostics(string TaskId, AgentDiagnostics Diagnostics, IngestNotes IngestNotes, double LatencyMs);
