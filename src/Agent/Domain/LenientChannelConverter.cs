using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agent.Domain;

// A3 and playbook step 40: a channel name the program does not know is the Unknown value,
// never a parse failure. Reads any casing of the member names; writes camelCase, the wire
// spelling the samples use ("sms", "email").
public sealed class LenientChannelConverter : JsonConverter<CommunicationChannel>
{
    public override CommunicationChannel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Enum.TryParse also accepts a purely numeric string and casts it to the
        // underlying ordinal (e.g. "1" -> Sms) - excluded up front so a numeric channel
        // name is never mistaken for the real channel that happens to share its ordinal.
        if (reader.TokenType == JsonTokenType.String
            && reader.GetString() is { } value
            && !int.TryParse(value, out _)
            && Enum.TryParse(value, ignoreCase: true, out CommunicationChannel channel)
            && Enum.IsDefined(channel))
        {
            return channel;
        }

        return CommunicationChannel.Unknown;
    }

    public override void Write(Utf8JsonWriter writer, CommunicationChannel value, JsonSerializerOptions options) =>
        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}
