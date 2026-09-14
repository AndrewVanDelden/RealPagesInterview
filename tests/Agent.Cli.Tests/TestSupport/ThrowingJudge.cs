using Agent.Evaluation;

namespace Agent.Cli.Tests.TestSupport;

// Fault-injection seam for a judge that throws: SemanticJudge.GradeAsync already catches every
// exception a real completion client can raise, so the only way to exercise CliRunner's own
// per-record isolation around the judge call is a fake that bypasses GradeAsync entirely.
internal sealed class ThrowingJudge : ISemanticJudge
{
    public Task<JudgeVerdict> GradeAsync(ScoredRun run, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException($"Injected judge fault for '{run.ProspectCase.TaskId}'.");

    public Task<Scorecard> JudgeAsync(Scorecard scorecard, IReadOnlyList<ScoredRun> runs, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Injected judge fault.");
}
