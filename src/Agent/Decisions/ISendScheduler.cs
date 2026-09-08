using Agent.Domain;

namespace Agent.Decisions;

public interface ISendScheduler
{
    DateTimeOffset Resolve(DateTimeOffset referenceTime, DateTimeOffset? lastInteraction, string? timeZoneId, CommunicationChannel channel);
}
