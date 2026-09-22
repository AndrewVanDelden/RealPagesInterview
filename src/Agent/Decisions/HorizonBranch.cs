namespace Agent.Decisions;

// A7: the horizon rule has exactly three branches, and every record takes one of them. An absent
// move date is NoMoveDate: the move-in timeline is the first question a leasing team qualifies a
// prospect on, so a record that does not state one is a different case from a move far off, and the
// labeled records follow it up differently. A past date is NoMoveDate too, since a date already gone
// states no move to plan for.
public enum HorizonBranch
{
    Short,
    Long,
    NoMoveDate,
}
