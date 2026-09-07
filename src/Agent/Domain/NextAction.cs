namespace Agent.Domain;

public sealed record NextAction(string Type, string? Name = null, int? Value = null);
