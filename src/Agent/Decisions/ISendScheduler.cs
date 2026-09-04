using Agent.Domain;

namespace Agent.Decisions;

public interface ISendScheduler
{
    DateTimeOffset Resolve(DateTimeOffset lastInteraction, string timeZoneId, CommunicationChannel channel);
}
