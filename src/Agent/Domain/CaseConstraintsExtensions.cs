namespace Agent.Domain;

public static class CaseConstraintsExtensions
{
    // An absent constraint is not required; only an explicit true is. One shared rule for
    // SafetyValidator, OpenAiMessageComposer, and Evaluator, instead of each re-testing
    // "== true" on its own.
    public static bool RequiresOptOutInstructions(this CaseConstraints constraints) =>
        constraints.IncludeOptOutInstructions == true;
}
