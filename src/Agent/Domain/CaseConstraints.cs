namespace Agent.Domain;

public sealed record CaseConstraints(
    bool NoPiiLeak,
    bool? NoSensitiveDiscrimination,
    bool IncludeOptOutInstructions,
    string PrimaryCta);
