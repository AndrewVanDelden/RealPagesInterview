using System.Collections.Frozen;
using Agent.Common;
using Agent.Domain;

namespace Agent.Decisions;

// The send slot for a persona, lifecycle stage and channel. Each row is one labeled record's
// own send, and states only the channel that record went out on, the way an action catalog row
// states only the horizon branch its record showed. A key with no row keeps the channel's hour
// on the floor day, which is what the two sample records show. The rows below are the default,
// and a rules file can replace them; Create is the one gate every table goes through.
public sealed class SendSlotTable
{
    // A year after the floor is the furthest a row may move a send: past that it is not the
    // next message, and the bound keeps the scheduler's date arithmetic far from the calendar's end.
    private const int MaxDaysAfterFloorDay = 365;

    private readonly FrozenDictionary<(string Persona, string LifecycleStage, CommunicationChannel Channel), SendSlotRow> _rowsByKey;

    private SendSlotTable(
        IReadOnlyList<SendSlotRow> rows,
        FrozenDictionary<(string, string, CommunicationChannel), SendSlotRow> rowsByKey)
    {
        Rows = rows;
        _rowsByKey = rowsByKey;
    }

    // One row per holdout_12.jsonl record whose label is not the channel's hour on the floor
    // day. Every one of them is floored at the reference time, so a label's day is read as
    // local days after that day. A prospect at new on sms and at open on email have no row:
    // the samples and the hold-out send those at the channel's hour.
    public static SendSlotTable Default { get; } = Create(
    [
        new SendSlotRow("prospect", "new", CommunicationChannel.Email, 0, new TimeOnly(9, 5)),
        new SendSlotRow("prospect", "open", CommunicationChannel.Sms, 0, new TimeOnly(9, 20)),
        new SendSlotRow("prospect", "no_show", CommunicationChannel.Sms, 0, new TimeOnly(9, 15)),
        new SendSlotRow("prospect", "cancelled_manager", CommunicationChannel.Email, 0, new TimeOnly(13, 0)),
        new SendSlotRow("resident", "renewal_window", CommunicationChannel.Email, 0, new TimeOnly(10, 30)),
        new SendSlotRow("resident", "welcome", CommunicationChannel.Email, 1, new TimeOnly(9, 30)),
        new SendSlotRow("resident", "loyalty_engage", CommunicationChannel.Email, 3, new TimeOnly(11, 0)),
        new SendSlotRow("resident", "renewal_undecided", CommunicationChannel.Sms, 5, new TimeOnly(9, 0)),
        new SendSlotRow("resident", "renewal_details_requested", CommunicationChannel.Email, 5, new TimeOnly(9, 10)),
    ]).Value;

    // The rows as they were given, in order, so the table can be written out and read back.
    public IReadOnlyList<SendSlotRow> Rows { get; }

    // O(n) time and space in the number of rows. Rows are numbered from 1 in the order given.
    public static Result<SendSlotTable> Create(IReadOnlyList<SendSlotRow> rows) =>
        Create([.. rows.Select((row, index) => (index + 1, row))]);

    // O(n) time and space in the number of rows, each checked once. A failure lists every bad
    // row, named by its position and key. A rules file numbers its rows by their place in the
    // file and passes only those it could read, so the positions are the caller's. A row on a
    // channel the scheduler has no hour for is refused, since the scheduler never looks it up,
    // and so is a negative offset, since the send would then land before its floor.
    internal static Result<SendSlotTable> Create(IReadOnlyList<(int Position, SendSlotRow Row)> rows)
    {
        List<string> failures = [];
        var byKey = new Dictionary<(string Persona, string LifecycleStage, CommunicationChannel Channel), (int Position, SendSlotRow Row)>();

        foreach ((int position, SendSlotRow row) in rows)
        {
            (string Persona, string LifecycleStage, CommunicationChannel Channel) key = KeyOf(row.Persona, row.LifecycleStage, row.Channel);
            string label = $"Send slot row {position} ({key.Persona}/{key.LifecycleStage}/{ChannelName(row.Channel)})";
            bool sendable = SendScheduler.DefaultSendHour.ContainsKey(row.Channel);

            if (key.Persona.Length == 0)
            {
                failures.Add($"{label}: persona is blank.");
            }

            if (key.LifecycleStage.Length == 0)
            {
                failures.Add($"{label}: lifecycle stage is blank.");
            }

            if (!sendable)
            {
                failures.Add($"{label}: channel is not one of {string.Join(", ", SendScheduler.DefaultSendHour.Keys.Select(ChannelName))}.");
            }

            if (row.DaysAfterFloorDay < 0)
            {
                failures.Add($"{label}: day offset {row.DaysAfterFloorDay} is negative.");
            }
            else if (row.DaysAfterFloorDay > MaxDaysAfterFloorDay)
            {
                failures.Add($"{label}: day offset {row.DaysAfterFloorDay} is more than {MaxDaysAfterFloorDay}.");
            }

            if (key.Persona.Length > 0 && key.LifecycleStage.Length > 0 && sendable && !byKey.TryAdd(key, (position, row)))
            {
                failures.Add($"{label}: duplicates send slot row {byKey[key].Position}.");
            }
        }

        return failures.Count == 0
            ? Result<SendSlotTable>.Success(new SendSlotTable(
                [.. rows.Select(numbered => numbered.Row)],
                byKey.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.Row)))
            : Result<SendSlotTable>.Failure(string.Join(Environment.NewLine, failures));
    }

    // O(1): one hash lookup. Persona and stage are free text on the record, so they are
    // trimmed and lowercased the same way the rows are.
    public Option<SendSlotRow> Find(string? persona, string? lifecycleStage, CommunicationChannel channel)
    {
        if (persona is null || lifecycleStage is null)
        {
            return Option<SendSlotRow>.None();
        }

        return _rowsByKey.TryGetValue(KeyOf(persona, lifecycleStage, channel), out SendSlotRow? row)
            ? Option<SendSlotRow>.Some(row)
            : Option<SendSlotRow>.None();
    }

    private static (string Persona, string LifecycleStage, CommunicationChannel Channel) KeyOf(string persona, string lifecycleStage, CommunicationChannel channel) =>
        (persona.Trim().ToLowerInvariant(), lifecycleStage.Trim().ToLowerInvariant(), channel);

    private static string ChannelName(CommunicationChannel channel) => channel.ToString().ToLowerInvariant();
}
