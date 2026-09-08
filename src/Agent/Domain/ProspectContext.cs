using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agent.Domain;

// Every member is optional (D1): a decision that needs one and finds null applies the
// assumption that names the default (A4, A6, A7, A12, A13) and the ingest notes name the field.
public sealed record ProspectContext(
    string? PropertyName = null,
    DateOnly? MoveDateTarget = null,
    DateTimeOffset? LastInteraction = null,
    [property: JsonPropertyName("timezone")] string? TimeZoneId = null,
    string? Language = null,
    ProspectProfile? Profile = null)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? UnknownMembers { get; init; }

    public ProspectProfile ProfileOrEmpty => Profile ?? new ProspectProfile();
}
