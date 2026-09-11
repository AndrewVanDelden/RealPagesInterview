namespace Agent.Common;

// A local send slot resolved against a zone's rules. The instant always carries the offset
// the zone was actually on at that instant, never the offset read at the wall time: a wrong
// offset travels with the value, so no reader downstream could detect it. Resolution says
// what the zone's rules did to the wall time to get there.
public sealed record ResolvedSlot(DateTimeOffset Instant, SlotResolution Resolution);
