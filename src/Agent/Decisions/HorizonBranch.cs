namespace Agent.Decisions;

// A7: the horizon rule has exactly two branches, and every record takes one of them. An
// absent move date is Long (no date, no cadence to start); a past date is Short (the move
// is due), so there is no third "unknown" branch to model.
public enum HorizonBranch
{
    Short,
    Long,
}
