namespace Agent.Evaluation;

// The seam CliRunner grades through: one real implementation, SemanticJudge, and one offline
// implementation per test that needs to control or fault-inject a grade, the same seam-per-
// external-dependency shape every non-deterministic dependency in this program takes.
public interface ISemanticJudge
{
    Task<JudgeVerdict> GradeAsync(ScoredRun run, CancellationToken cancellationToken = default);

    Task<Scorecard> JudgeAsync(Scorecard scorecard, IReadOnlyList<ScoredRun> runs, CancellationToken cancellationToken = default);
}
