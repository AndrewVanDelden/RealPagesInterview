using Agent.Domain;

namespace Agent.Evaluation;

// One case's already-executed output, captured once during the batch pass and reused for
// scoring: the evaluator never re-runs the agent, so the report describes exactly what was
// persisted to --output. SafetyViolationCount and LatencyMs exist only in the run that
// produced the output; replay passes null for both and they score as not measured.
public sealed record ScoredRun(ProspectCase ProspectCase, AgentOutput Output, int? SafetyViolationCount, double? LatencyMs);
