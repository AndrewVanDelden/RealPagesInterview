namespace Agent.Decisions;

// What a rules file holds once read and checked: the action catalog the planner takes and the
// send-slot table the scheduler takes, each in place of the compiled one.
public sealed record DecisionRules(ActionCatalog Catalog, SendSlotTable SendSlots);
