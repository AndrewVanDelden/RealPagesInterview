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
        Converters = { new LenientChannelConverter() },
        RespectNullableAnnotations = true,
        // RespectNullableAnnotations rejects only an explicit null. A property that is
        // absent from the JSON binds silently (a DateOnly to 0001-01-01, a bool to false,
        // an object to null), which produced the year-0001 plans on the real hold-out.
        // With this on, every constructor parameter without a default value is required
        // and its absence is a JsonException. Optional members carry "= null" defaults, or
        // a [JsonConstructor] listing only the required members where C# parameter order
        // forbids a default.
        RespectRequiredConstructorParameters = true,
        // The default encoder escapes ordinary punctuation and every non-ASCII character
        // to \uXXXX as an HTML/XSS precaution. None of our JSON is ever embedded in a web
        // page - it is JSONL on ingestion and a formatted JSON array on output, both read
        // by humans and by our own reader - so that precaution only makes composed message
        // text unreadable for no benefit.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
