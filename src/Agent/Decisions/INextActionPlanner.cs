namespace Agent.Decisions;

public interface INextActionPlanner
{
    PlannedAction Plan(string? persona, string? lifecycleStage, DateOnly? moveDateTarget, DateOnly referenceDate);
}
