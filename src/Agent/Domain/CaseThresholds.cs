using System.Text.Json.Serialization;

namespace Agent.Domain;

public sealed record CaseThresholds(
    [property: JsonPropertyName("p95_latency_ms")] int P95LatencyMs,
    double PersonalizationScoreMin,
    [property: JsonPropertyName("reply_classification_f1_min")] double ReplyClassificationF1Min,
    int SafetyViolationsMax);
