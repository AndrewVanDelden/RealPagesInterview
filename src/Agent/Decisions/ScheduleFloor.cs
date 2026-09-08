namespace Agent.Decisions;

// A4: the send day is counted from max(reference time, last_interaction), so exactly one of
// the two is the floor, and which one it was is the first thing a reader of an unexpected
// send_at needs (D22).
public enum ScheduleFloor
{
    // The record states no last interaction, or states one at or before the reference time.
    ReferenceTime,

    // The record's last interaction is after the reference time, so it moved the send day.
    LastInteraction,
}
