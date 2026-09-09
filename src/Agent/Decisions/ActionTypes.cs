using System.Collections.Frozen;

namespace Agent.Decisions;

// A8 and playbook step 42: the whole next_action vocabulary, named once. Nothing else in
// the program spells an action type as a literal, and ActionCatalog.Create refuses a row
// whose action type is not in All, so a typo in the table is a failure at construction
// instead of a value the evaluator silently scores as a miss.
public static class ActionTypes
{
    // Sample 1: a prospect 32 days from the move date.
    public const string StartCadence = "start_cadence";

    // Sample 2: a prospect 68 days out.
    public const string FollowUpInDays = "follow_up_in_days";

    // D2: the action on a record that is not contactable. No catalog row emits it; the agent
    // emits it at step 1, when the channel selector returns no value (D57), before the
    // planner runs.
    public const string NoOp = "no_op";

    public static readonly FrozenSet<string> All =
        new[] { StartCadence, FollowUpInDays, NoOp }.ToFrozenSet(StringComparer.Ordinal);
}
