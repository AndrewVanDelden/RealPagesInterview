using Agent.Domain;

namespace Agent.Orchestration;

// RejectedDraft is the draft the final safety gate suppressed, and is null on every other
// outcome. It is a third member rather than a field on AgentDiagnostics because the
// review queue is its own output, written whether or not the run asked for diagnostics.
public sealed record AgentRunResult(AgentOutput Output, AgentDiagnostics Diagnostics, RejectedDraft? RejectedDraft = null);
