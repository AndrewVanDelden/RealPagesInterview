using Agent.Evaluation;
using Agent.Ingest;

namespace Agent.Orchestration;

// LatencyMs is the wall-clock elapsed of exactly one LeasingMessageAgent.RunAsync call,
// measured by the batch loop that made it and handed from one variable to this row and to
// ScoredRun.LatencyMs, so the diagnostics file and the eval report can never state two
// latencies for one record. It excludes reading and parsing the input line, IngestNotes.Describe,
// every output write, the evaluator and the judge. Non-nullable: a record whose RunAsync threw
// gets no diagnostics row at all, so there is no row here without a measurement behind it. The
// batch's own elapsed is not here (this file is one row per unit of work); it is on the scorecard
// and the Batch complete log line. Judge is the semantic judge's verdict, its reason and its cost
// on a run that passed --judge, graded after LatencyMs was measured; null on a run without it.
public sealed record TaskDiagnostics(string TaskId, AgentDiagnostics Diagnostics, IngestNotes IngestNotes, double LatencyMs, JudgeVerdict? Judge = null);
