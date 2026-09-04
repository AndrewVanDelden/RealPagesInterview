namespace Agent.Safety;

public sealed record SafetyValidationResult(IReadOnlyList<string> Violations, bool FairHousingCheckPassed);
