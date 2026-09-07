namespace Agent.Domain;

public sealed record CaseThresholds(
    int P95LatencyMs,
    double PersonalizationScoreMin,
    double ReplyClassificationF1Min,
    int SafetyViolationsMax);
