using System.Text.Encodings.Web;
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
        // The default encoder escapes ordinary punctuation and every non-ASCII character
        // to \uXXXX as an HTML/XSS precaution. None of our JSON is ever embedded in a web
        // page - it is a JSONL file read by humans and by our own reader - so that
        // precaution only makes composed message text unreadable for no benefit.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
