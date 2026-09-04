namespace Agent.Domain;

public sealed record NextAction(string Type, string? Name, int? Value);
