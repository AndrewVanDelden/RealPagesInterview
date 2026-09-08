using System.Text.Json.Serialization;
using Agent.Common;
using Agent.Decisions;

namespace Agent.Orchestration;

// D22: how send_at was reached, for the diagnostics file, the way ActionPlanNotes carries
// how next_action was reached (D18). One object that is present or absent: a record with no
// message was never scheduled, so it has no floor, no zone and no slot. The converters are
// attached here rather than to the enums themselves, because ScheduleFloor and
// SlotResolution are decision values and how the diagnostics file spells them is this
// record's business.
public sealed record ScheduleNotes(
    [property: JsonConverter(typeof(SnakeCaseLowerEnumConverter<ScheduleFloor>))] ScheduleFloor Floor,
    string TimeZoneId,
    [property: JsonConverter(typeof(SnakeCaseLowerEnumConverter<SlotResolution>))] SlotResolution Slot);
