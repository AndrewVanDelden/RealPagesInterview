using Agent.Domain;
using Agent.Ingest;

namespace Agent.Evaluation;

// One case's already-executed output, captured once during the batch pass and reused for
// scoring: the evaluator never re-runs the agent, so the report describes exactly what was
// persisted to --output. SafetyViolationCount and LatencyMs exist only in the run that
// produced the output; replay passes null for both and they score as not measured.
//
// The case is held as the SanitizedInput its caller cleaned, not as a bare record, so a run
// cannot be built from a record nobody cleaned: the scorer and the judge both read prospect
// text, and both read it here rather than cleaning it again on a path the caller already
// cleaned once. ProspectCase is that cleaned record, the one every reader scores against.
public sealed record ScoredRun(SanitizedInput SanitizedCase, AgentOutput Output, int? SafetyViolationCount, double? LatencyMs)
{
    public ProspectCase ProspectCase => SanitizedCase.Case;
}
