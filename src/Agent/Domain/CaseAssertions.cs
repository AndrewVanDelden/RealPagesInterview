using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agent.Domain;

public sealed record CaseAssertions(IReadOnlyList<string>? RequiredStates = null, CaseConstraints? Constraints = null)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? UnknownMembers { get; init; }
}
