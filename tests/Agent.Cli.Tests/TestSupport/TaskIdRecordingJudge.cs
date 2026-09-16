using Agent.Evaluation;

namespace Agent.Cli.Tests.TestSupport;

// A judge that grades nothing and keeps the task id of every run it is handed, so a test can read the
// record exactly as the judge, and every line the judge logs about it, would see it.
internal sealed class TaskIdRecordingJudge : ISemanticJudge
{
    public List<string> JudgedTaskIds { get; } = [];

    public Task<JudgeVerdict> GradeAsync(ScoredRun run, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("A replay judges through JudgeAsync.");

    public Task<Scorecard> JudgeAsync(Scorecard scorecard, IReadOnlyList<ScoredRun> runs, CancellationToken cancellationToken = default)
    {
        JudgedTaskIds.AddRange(runs.Select(run => run.ProspectCase.TaskId));
        return Task.FromResult(scorecard);
    }
}
