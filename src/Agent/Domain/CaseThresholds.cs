namespace Agent.Domain;

// Every threshold is optional (D1, A15): one the record does not state is not enforced,
// except the safety budget, whose absence means zero.
public sealed record CaseThresholds(
    int? P95LatencyMs = null,
    double? PersonalizationScoreMin = null,
    double? ReplyClassificationF1Min = null,
    int? SafetyViolationsMax = null) : HasUnknownMembers;
