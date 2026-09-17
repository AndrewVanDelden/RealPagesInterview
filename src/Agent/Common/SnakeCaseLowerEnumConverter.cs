using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agent.Common;

// One converter for every enum the wire spells in snake_case (suppression_reason values such
// as no_contact_consent, and the diagnostics' enums), so a third enum needing that spelling
// is a usage of this generic, not another JsonStringEnumConverter subclass.
public sealed class SnakeCaseLowerEnumConverter<TEnum> : JsonStringEnumConverter<TEnum>
    where TEnum : struct, Enum
{
    public SnakeCaseLowerEnumConverter()
        : base(JsonNamingPolicy.SnakeCaseLower)
    {
    }

    // The same spelling this converter writes to the wire, for code that needs the wire name as a
    // string rather than through JSON serialization.
    public static string ToWireName(TEnum value) =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
