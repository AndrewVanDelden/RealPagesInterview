using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agent.Domain;

// A16: members the record types do not declare, kept so diagnostics can name them.
// Every optional-member input record shares this one declaration instead of repeating it
// (Earned Abstraction: the seventh occurrence is the one too many).
public abstract record HasUnknownMembers
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? UnknownMembers { get; init; }
}
