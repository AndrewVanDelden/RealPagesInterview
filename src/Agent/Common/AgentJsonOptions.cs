using System.Text.Json;
using System.Text.Json.Serialization;
using Agent.Domain;

namespace Agent.Common;

public static class AgentJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter<CommunicationChannel>(JsonNamingPolicy.CamelCase) },
        RespectNullableAnnotations = true,
    };
}
