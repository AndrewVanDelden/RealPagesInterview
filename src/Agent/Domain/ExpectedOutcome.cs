using System.Text.Json.Serialization;

namespace Agent.Domain;

public sealed record ExpectedOutcome(NextMessage? NextMessage, NextAction NextAction)
{
    // System.Text.Json binds through this constructor: NextAction is required, NextMessage
    // binds through its init setter when present (see AgentJsonOptions on
    // RespectRequiredConstructorParameters). C# forbids a default value before a required
    // parameter, so the positional order above cannot express this.
    [JsonConstructor]
    public ExpectedOutcome(NextAction nextAction)
        : this(NextMessage: null, nextAction)
    {
    }
}
