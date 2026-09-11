using Agent.Common;

namespace Agent.Decisions;

// The scheduler returns why, not just when, the way the planner does: which input was the
// floor, the zone the slot was resolved in, what the zone's rules did to the slot, and which
// rule chose the day and the time. TimeZoneId is the resolved zone's own id, so an
// unrecognized value from the record reads UTC here and is named once, in the ingest notes.
public sealed record ScheduledSend(
    DateTimeOffset SendAt,
    ScheduleFloor Floor,
    string TimeZoneId,
    SlotResolution Slot,
    SendSlotSource Source);
