using System.Collections.Frozen;
using Agent.Common;
using Agent.Domain;

namespace Agent.Decisions;

// The send slot for a persona, lifecycle stage and channel. Each row is one labeled record's
// own send, and states only the channel that record went out on, the way an action catalog row
// states only the horizon branch its record showed. A key with no row keeps the channel's hour
// on the floor day, which is what the two sample records show.
public static class SendSlotTable
{
    // One row per holdout_12.jsonl record whose label is not the channel's hour on the floor
    // day. Every one of them is floored at the reference time, so a label's day is read as
    // local days after that day. A prospect at new on sms and at open on email have no row:
    // the samples and the hold-out send those at the channel's hour.
    public static IReadOnlyList<SendSlotRow> Rows { get; } =
    [
        new("prospect", "new", CommunicationChannel.Email, 0, new TimeOnly(9, 5)),
        new("prospect", "open", CommunicationChannel.Sms, 0, new TimeOnly(9, 20)),
        new("prospect", "no_show", CommunicationChannel.Sms, 0, new TimeOnly(9, 15)),
        new("prospect", "cancelled_manager", CommunicationChannel.Email, 0, new TimeOnly(13, 0)),
        new("resident", "renewal_window", CommunicationChannel.Email, 0, new TimeOnly(10, 30)),
        new("resident", "welcome", CommunicationChannel.Email, 1, new TimeOnly(9, 30)),
        new("resident", "loyalty_engage", CommunicationChannel.Email, 3, new TimeOnly(11, 0)),
        new("resident", "renewal_undecided", CommunicationChannel.Sms, 5, new TimeOnly(9, 0)),
        new("resident", "renewal_details_requested", CommunicationChannel.Email, 5, new TimeOnly(9, 10)),
    ];

    private static readonly FrozenDictionary<(string Persona, string LifecycleStage, CommunicationChannel Channel), SendSlotRow> RowsByKey =
        Rows.ToFrozenDictionary(row => KeyOf(row.Persona, row.LifecycleStage, row.Channel));

    // O(1): one hash lookup. Persona and stage are free text on the record, so they are
    // trimmed and lowercased the same way the rows are.
    public static Option<SendSlotRow> Find(string? persona, string? lifecycleStage, CommunicationChannel channel)
    {
        if (persona is null || lifecycleStage is null)
        {
            return Option<SendSlotRow>.None();
        }

        return RowsByKey.TryGetValue(KeyOf(persona, lifecycleStage, channel), out SendSlotRow? row)
            ? Option<SendSlotRow>.Some(row)
            : Option<SendSlotRow>.None();
    }

    private static (string Persona, string LifecycleStage, CommunicationChannel Channel) KeyOf(string persona, string lifecycleStage, CommunicationChannel channel) =>
        (persona.Trim().ToLowerInvariant(), lifecycleStage.Trim().ToLowerInvariant(), channel);
}
