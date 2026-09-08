using Agent.Common;
using Agent.Domain;

namespace Agent.Decisions;

// A4 and A5: the channel's slot on the first day at or after max(reference time,
// last_interaction) in the record's timezone; an absent or unknown timezone is UTC (A6).
// The slot is a wall time, and a zone does not reach every wall time exactly once, so
// TimeZones.ResolveSlot turns it into an instant and says what the zone's rules did (A20).
// No quiet-hours window and no minutes: DESIGN.md section 8 and docs/CODE_REVIEW.md.
public sealed class SendScheduler : ISendScheduler
{
    private static readonly IReadOnlyDictionary<CommunicationChannel, TimeOnly> DefaultSendHour = new Dictionary<CommunicationChannel, TimeOnly>
    {
        [CommunicationChannel.Sms] = new TimeOnly(9, 0),
        [CommunicationChannel.Email] = new TimeOnly(10, 0),
        [CommunicationChannel.Voice] = new TimeOnly(9, 0),
    };

    public ScheduledSend Resolve(DateTimeOffset referenceTime, DateTimeOffset? lastInteraction, string? timeZoneId, CommunicationChannel channel)
    {
        if (!DefaultSendHour.TryGetValue(channel, out TimeOnly defaultHour))
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown communication channel.");
        }

        TimeZoneInfo timeZone = TimeZones.ResolveOrUtc(timeZoneId);
        DateTimeOffset floor = referenceTime;
        var floorSource = ScheduleFloor.ReferenceTime;

        if (lastInteraction is { } last && last > referenceTime)
        {
            floor = last;
            floorSource = ScheduleFloor.LastInteraction;
        }

        DateTime localFloor = TimeZoneInfo.ConvertTime(floor, timeZone).DateTime;

        DateTime candidateLocal = DateOnly.FromDateTime(localFloor).ToDateTime(defaultHour);

        if (candidateLocal <= localFloor)
        {
            candidateLocal = candidateLocal.AddDays(1);
        }

        ResolvedSlot slot = TimeZones.ResolveSlot(timeZone, candidateLocal);

        return new ScheduledSend(
            slot.Instant,
            floorSource,
            timeZone.Id,
            slot.Resolution);
    }
}
