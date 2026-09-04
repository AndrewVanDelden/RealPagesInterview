using Agent.Domain;

namespace Agent.Decisions;

public interface INextActionPlanner
{
    NextAction Plan(DateOnly moveDateTarget, DateOnly lastInteractionDate);
}
