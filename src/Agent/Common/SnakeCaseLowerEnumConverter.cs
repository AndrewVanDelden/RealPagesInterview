using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agent.Common;

// One converter for every enum the wire spells in snake_case (D3), so a third enum needing
// that spelling is a usage of this generic, not another JsonStringEnumConverter subclass.
public sealed class SnakeCaseLowerEnumConverter<TEnum> : JsonStringEnumConverter<TEnum>
    where TEnum : struct, Enum
{
    public SnakeCaseLowerEnumConverter()
        : base(JsonNamingPolicy.SnakeCaseLower)
    {
    }
}
