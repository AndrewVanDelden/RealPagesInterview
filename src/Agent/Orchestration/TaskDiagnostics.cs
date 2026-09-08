using Agent.Ingest;

namespace Agent.Orchestration;

public sealed record TaskDiagnostics(string TaskId, AgentDiagnostics Diagnostics, IngestNotes IngestNotes);
