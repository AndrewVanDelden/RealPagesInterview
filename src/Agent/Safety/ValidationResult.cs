namespace Agent.Safety;

public sealed record ValidationResult(IReadOnlyList<string> Violations, bool FairHousingCheckPassed);
