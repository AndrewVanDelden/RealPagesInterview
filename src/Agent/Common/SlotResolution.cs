namespace Agent.Common;

// A20: how a local send slot became an instant. Exact is every record the current zone
// database can produce, since no transition covers 09:00 or 10:00 (A5); the other two are
// the transition days. A slot the zone skips resolves to the first instant at or after it,
// never earlier than the slot and closest to its hour; a slot the zone repeats resolves to
// the earlier of the two. They are values rather than a silent correction so the diagnostics
// can say which one happened.
public enum SlotResolution
{
    // The zone reached that wall time exactly once.
    Exact,

    // The zone sprang forward across the slot, so that wall time never happened; the instant
    // is the one past the gap, carrying the offset the zone was on when it got there.
    ShiftedPastGap,

    // The zone fell back across the slot, so that wall time happened twice; the instant is
    // the earlier of the two.
    EarlierOfTwo,
}
