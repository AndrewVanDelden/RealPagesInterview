using System.Text.Json.Serialization;

namespace Agent.Domain;

public sealed record AgentOutput(NextMessage? NextMessage, NextAction NextAction)
{
    // Same shape and same reason as ExpectedOutcome: NextAction is required on the wire,
    // NextMessage is optional, and the positional order cannot say so with a default.
    [JsonConstructor]
    public AgentOutput(NextAction nextAction)
        : this(NextMessage: null, nextAction)
    {
    }
}
