using System.Text.Json.Serialization;

namespace Agent.Domain;

public sealed record CaseConstraints(
    bool NoPiiLeak,
    bool? NoSensitiveDiscrimination,
    bool IncludeOptOutInstructions,
    string? PrimaryCta)
{
    // System.Text.Json binds through this constructor: its two parameters are the required
    // members, the other two bind through their init setters when present (see
    // AgentJsonOptions on RespectRequiredConstructorParameters). C# forbids a default value
    // before a required parameter, so the positional order above cannot express this.
    [JsonConstructor]
    public CaseConstraints(bool noPiiLeak, bool includeOptOutInstructions)
        : this(noPiiLeak, NoSensitiveDiscrimination: null, includeOptOutInstructions, PrimaryCta: null)
    {
    }
}
