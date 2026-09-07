namespace Agent.Domain;

public sealed record Cta(string Type, IReadOnlyList<string>? Options = null, Uri? Link = null);
