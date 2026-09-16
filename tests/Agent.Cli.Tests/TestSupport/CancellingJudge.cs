using Agent.Evaluation;

namespace Agent.Cli.Tests.TestSupport;

// Fault-injection seam for a judge whose call ends in a cancellation the run never asked for, the
// way a real completion client's own timeout would: it never observes the token it is given, so
// this proves CliRunner's own boundary around the judge call treats that cancellation the same as
// any other judge fault rather than letting it end the record's own already-produced output.
internal sealed class CancellingJudge : ISemanticJudge
{
    public Task<JudgeVerdict> GradeAsync(ScoredRun run, CancellationToken cancellationToken = default) =>
        throw new OperationCanceledException($"Injected judge cancellation for '{run.ProspectCase.TaskId}'.");

    public Task<Scorecard> JudgeAsync(Scorecard scorecard, IReadOnlyList<ScoredRun> runs, CancellationToken cancellationToken = default) =>
        throw new OperationCanceledException("Injected judge cancellation.");
}
