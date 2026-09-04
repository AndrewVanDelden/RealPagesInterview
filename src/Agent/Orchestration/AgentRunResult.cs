using Agent.Domain;

namespace Agent.Orchestration;

public sealed record AgentRunResult(AgentOutput Output, AgentDiagnostics Diagnostics);
