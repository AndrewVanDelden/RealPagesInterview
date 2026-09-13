using Agent.Domain;

namespace Agent.Decisions;

// Playbook step 39: the planner returns why, not just what. It returns no Result: every
// lookup lands on a row or the generic row, so a malformed catalog is refused once, by
// ActionCatalog.Create, and not per record. Branch and HorizonDays are the horizon rule's own
// working (A7); HorizonDays is null when the record states no move date, which is unstated
// rather than zero. Source names the row that supplied the action, so a generic-row fallback
// is visible in the diagnostics instead of looking like a decision the catalog made.
public sealed record PlannedAction(
    NextAction Action,
    HorizonBranch Branch,
    int? HorizonDays,
    ActionSource Source);
