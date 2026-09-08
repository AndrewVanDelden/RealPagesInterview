using Agent.Domain;

namespace Agent.Decisions;

public interface ISendScheduler
{
    ScheduledSend Resolve(DateTimeOffset referenceTime, DateTimeOffset? lastInteraction, string? timeZoneId, CommunicationChannel channel);
}
