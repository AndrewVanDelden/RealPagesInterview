using Agent.Common;

namespace Agent.Decisions;

// D22: the scheduler returns why, not just when, the way the planner does (D18). The three
// inputs of A4, A5 and A6 are the three members: which input was the floor, the zone the
// slot was resolved in, and what the zone's rules did to the slot (A20). TimeZoneId is the
// resolved zone's own id, so an unrecognized value from the record reads UTC here and is
// named once, in the ingest notes.
public sealed record ScheduledSend(
    DateTimeOffset SendAt,
    ScheduleFloor Floor,
    string TimeZoneId,
    SlotResolution Slot);
