using Agent.Composition;

namespace Agent.Evaluation;

// One record's grades from the semantic judge, the reason it gave for them, and what grading
// cost. It is written on the record's diagnostics row and folded onto its score row, so a failed
// grade says why wherever it is read. ModelCost is null only when no call was made, which is a
// record with no label to grade against.
public sealed record JudgeVerdict(CheckResult ActionSemantic, CheckResult BodySemantic, string? Reason, ModelCostNotes? ModelCost);
