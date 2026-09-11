namespace Agent.Safety;

// One check's own answer, never a shared boolean. Details are the violation lines
// this check contributes to the flat list on SafetyValidationResult; a Passed or a
// NotApplicable check contributes none, so the three factories below are the only way to
// build one and a verdict can never disagree with its details.
public sealed record SafetyCheckResult(SafetyCheck Check, SafetyCheckVerdict Verdict, IReadOnlyList<string> Details)
{
    public static SafetyCheckResult Passed(SafetyCheck check) => new(check, SafetyCheckVerdict.Passed, []);

    public static SafetyCheckResult NotApplicable(SafetyCheck check) => new(check, SafetyCheckVerdict.NotApplicable, []);

    public static SafetyCheckResult Failed(SafetyCheck check, IReadOnlyList<string> details) =>
        new(check, SafetyCheckVerdict.Failed, details);
}
