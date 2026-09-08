using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agent.Domain;

// Every constraint is optional (D1); an absent one is not required, so a consumer tests
// for "== true", never for the bare value.
public sealed record CaseConstraints(
    bool? NoPiiLeak = null,
    bool? NoSensitiveDiscrimination = null,
    bool? IncludeOptOutInstructions = null,
    string? PrimaryCta = null)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? UnknownMembers { get; init; }
}
