using Agent.Common;
using Agent.Domain;

namespace Agent.Decisions;

// The floor is max(reference time, last_interaction) in the record's timezone, and an absent
// or unknown timezone is UTC. The slot table's row for the persona, stage and channel moves the
// send that many local days past the floor's day and sets its local time; with no row the send
// is the channel's hour on the floor day. A same-day slot already passed rolls to the next day.
// The slot is a wall time a zone may skip or repeat, so TimeZones.ResolveSlot turns it into an
// instant and says what the zone did. No quiet-hours window: DESIGN.md section 8. The slot
// table is a value the caller can substitute, and the compiled table is the default.
public sealed class SendScheduler(SendSlotTable? slotTable = null)
{
    // The channels a message is sent on, each with its hour; a slot row may name only these.
    internal static readonly IReadOnlyDictionary<CommunicationChannel, TimeOnly> DefaultSendHour = new Dictionary<CommunicationChannel, TimeOnly>
    {
        [CommunicationChannel.Sms] = new TimeOnly(9, 0),
        [CommunicationChannel.Email] = new TimeOnly(10, 0),
        [CommunicationChannel.Voice] = new TimeOnly(9, 0),
    };

    private readonly SendSlotTable _slotTable = slotTable ?? SendSlotTable.Default;

    public ScheduledSend Resolve(
        DateTimeOffset referenceTime,
        DateTimeOffset? lastInteraction,
        string? timeZoneId,
        CommunicationChannel channel,
        string? persona,
        string? lifecycleStage)
    {
        if (!DefaultSendHour.TryGetValue(channel, out TimeOnly defaultHour))
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown communication channel.");
        }

        Option<SendSlotRow> row = _slotTable.Find(persona, lifecycleStage, channel);

        (int daysAfterFloorDay, TimeOnly localTime, SendSlotSource source) = row.HasValue
            ? (row.Value.DaysAfterFloorDay, row.Value.LocalTime, SendSlotSource.SlotRow)
            : (0, defaultHour, SendSlotSource.ChannelDefault);

        TimeZoneInfo timeZone = TimeZones.ResolveOrUtc(timeZoneId);
        DateTimeOffset floor = referenceTime;
        var floorSource = ScheduleFloor.ReferenceTime;

        if (lastInteraction is { } last && last > referenceTime)
        {
            floor = last;
            floorSource = ScheduleFloor.LastInteraction;
        }

        DateTime localFloor = TimeZoneInfo.ConvertTime(floor, timeZone).DateTime;

        DateTime candidateLocal = DateOnly.FromDateTime(localFloor).AddDays(daysAfterFloorDay).ToDateTime(localTime);

        if (candidateLocal <= localFloor)
        {
            candidateLocal = candidateLocal.AddDays(1);
        }

        ResolvedSlot slot = TimeZones.ResolveSlot(timeZone, candidateLocal);

        return new ScheduledSend(
            slot.Instant,
            floorSource,
            timeZone.Id,
            slot.Resolution,
            source);
    }
}
