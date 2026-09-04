namespace Agent.Domain;

public sealed record AgentOutput(NextMessage? NextMessage, NextAction NextAction);
