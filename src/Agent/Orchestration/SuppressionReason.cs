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

public static class SuppressionReasonExtensions
{
    // The same snake_case spelling SuppressionReasonConverter writes to the wire, derived
    // once instead of hand-spelled wherever a reason also has to appear as next_action.reason.
    public static string ToWireName(this SuppressionReason reason) =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(reason.ToString());
}
