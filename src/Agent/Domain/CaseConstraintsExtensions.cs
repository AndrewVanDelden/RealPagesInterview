namespace Agent.Domain;

public static class CaseConstraintsExtensions
{
    // D1: an absent constraint is not required. One shared rule for SafetyValidator,
    // OpenAiMessageComposer, and Evaluator, instead of each re-testing "== true" on its own.
    public static bool RequiresOptOutInstructions(this CaseConstraints constraints) =>
        constraints.IncludeOptOutInstructions == true;
}
