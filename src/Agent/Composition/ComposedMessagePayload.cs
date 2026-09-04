namespace Agent.Composition;

internal sealed record ComposedMessagePayload(string? Subject, string? Body, string? CtaType, IReadOnlyList<string>? CtaOptions, Uri? CtaLink);
