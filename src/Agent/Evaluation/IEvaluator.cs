namespace Agent.Evaluation;

public interface IEvaluator
{
    Scorecard Evaluate(IReadOnlyList<ScoredRun> runs);
}
