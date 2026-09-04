using System.Text.Json.Serialization;

namespace Agent.Domain;

public sealed record ProspectContext(
    string PropertyName,
    DateOnly MoveDateTarget,
    DateTimeOffset LastInteraction,
    [property: JsonPropertyName("timezone")] string TimeZoneId,
    string Language,
    ProspectProfile Profile);
