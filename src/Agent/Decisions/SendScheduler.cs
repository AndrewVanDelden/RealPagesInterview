using Agent.Common;
using Agent.Domain;

namespace Agent.Decisions;

public sealed class SendScheduler : ISendScheduler
{
    private static readonly IReadOnlyDictionary<CommunicationChannel, TimeOnly> DefaultSendHour = new Dictionary<CommunicationChannel, TimeOnly>
    {
        [CommunicationChannel.Sms] = new TimeOnly(9, 0),
        [CommunicationChannel.Email] = new TimeOnly(10, 0),
        [CommunicationChannel.Voice] = new TimeOnly(9, 0),
    };

    public DateTimeOffset Resolve(DateTimeOffset lastInteraction, string timeZoneId, CommunicationChannel channel)
    {
        TimeZoneInfo timeZone = TimeZones.Resolve(timeZoneId);
        DateTimeOffset localLastInteraction = TimeZoneInfo.ConvertTime(lastInteraction, timeZone);

        if (!DefaultSendHour.TryGetValue(channel, out TimeOnly defaultHour))
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown communication channel.");
        }

        DateOnly candidateDate = DateOnly.FromDateTime(localLastInteraction.DateTime);
        DateTime candidateLocal = candidateDate.ToDateTime(defaultHour);

        if (candidateLocal <= localLastInteraction.DateTime)
        {
            candidateLocal = candidateLocal.AddDays(1);
        }

        TimeSpan offset = timeZone.GetUtcOffset(candidateLocal);
        return new DateTimeOffset(candidateLocal, offset);
    }
}
