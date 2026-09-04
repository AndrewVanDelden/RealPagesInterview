using Agent.Domain;

namespace Agent.Decisions;

public interface INextActionPlanner
{
    NextAction Plan(DateOnly moveDateTarget, DateTimeOffset lastInteraction, string timeZoneId);
}
