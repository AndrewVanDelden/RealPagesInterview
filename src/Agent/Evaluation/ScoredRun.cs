using Agent.Domain;
using Agent.Orchestration;

namespace Agent.Evaluation;

// One case's already-executed result, captured once during the main batch pass and reused
// for scoring - Evaluator never re-runs the agent, so the eval report describes exactly
// what was persisted to --output, not a second, possibly different, sample.
public sealed record ScoredRun(ProspectCase ProspectCase, AgentRunResult Result, double LatencyMs);
