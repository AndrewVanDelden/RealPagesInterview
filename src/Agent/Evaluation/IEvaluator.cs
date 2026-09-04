using Agent.Domain;

namespace Agent.Evaluation;

public interface IEvaluator
{
    Task<Scorecard> EvaluateAsync(IReadOnlyList<ProspectCase> cases, CancellationToken cancellationToken = default);
}
