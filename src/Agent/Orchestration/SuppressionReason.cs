using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agent.Orchestration;

// D3: why a record carries no message. None means a message was sent. Written to the
// diagnostics file in snake_case, the same spelling next_action.reason uses.
[JsonConverter(typeof(SuppressionReasonConverter))]
public enum SuppressionReason
{
    None,
    NoContactConsent,
    CompositionFailed,
    SafetyViolation,
}

public sealed class SuppressionReasonConverter : JsonStringEnumConverter<SuppressionReason>
{
    public SuppressionReasonConverter()
        : base(JsonNamingPolicy.SnakeCaseLower)
    {
    }
}
