using Agent.Common;
using Agent.Domain;
using Agent.Ingest;

namespace Agent.Evaluation;

// The output file carries no task id (nothing extra in the graded output), so replay
// pairs rows with the records that parsed by position, in input order, the order the writer
// appended them. Counts that differ mean the file was not produced from this input, or a
// record failed at runtime and left no row; either way the pairing is unknown and refused.
public static class ReplayAlignment
{
    // O(n) in the record count. The records arrive cleaned, as a live run's do, and are carried
    // onto each run as they are given rather than cleaned a second time.
    public static Result<IReadOnlyList<ScoredRun>> Align(IReadOnlyList<SanitizedInput> cases, IReadOnlyList<AgentOutput> outputs)
    {
        if (cases.Count != outputs.Count)
        {
            return Result<IReadOnlyList<ScoredRun>>.Failure(
                $"{cases.Count} record(s) parsed from --input but {outputs.Count} output(s) in the file; replay pairs them by position and cannot align these.");
        }

        return Result<IReadOnlyList<ScoredRun>>.Success(
            cases.Select((sanitizedCase, index) => new ScoredRun(sanitizedCase, outputs[index], SafetyViolationCount: null, LatencyMs: null)).ToList());
    }
}
