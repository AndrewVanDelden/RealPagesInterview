namespace Agent.Orchestration;

public sealed record AgentDiagnostics(
    bool ConsentVerified,
    bool FairHousingCheckPassed,
    bool BrandStyleApplied,
    int SafetyViolationCount);
