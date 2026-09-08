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
        if (reader.TokenType == JsonTokenType.String
            && Enum.TryParse(reader.GetString(), ignoreCase: true, out CommunicationChannel channel)
            && Enum.IsDefined(channel))
        {
            return channel;
        }

        return CommunicationChannel.Unknown;
    }

    public override void Write(Utf8JsonWriter writer, CommunicationChannel value, JsonSerializerOptions options) =>
        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}
