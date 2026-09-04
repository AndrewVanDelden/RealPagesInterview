namespace Agent.Orchestration;

// FairHousingCheckPassed is null when the safety validator never ran for this
// prospect (no-consent suppression, or the composer could not produce a message
// to validate) - null means "not evaluated", distinct from true/false which mean
// the check ran and recorded a result.
public sealed record AgentDiagnostics(
    bool ConsentVerified,
    bool? FairHousingCheckPassed,
    bool BrandStyleApplied,
    int SafetyViolationCount);
