namespace Agent.Decisions;

// Which rule set the send slot, so a reader of an unexpected send_at can tell a slot row
// from the channel's hour the way the action plan tells a catalog row from the generic row.
public enum SendSlotSource
{
    // The slot table has a row for the record's persona, lifecycle stage and channel.
    SlotRow,

    // No row answers that key, so the send is the channel's hour on the floor day.
    ChannelDefault,
}
