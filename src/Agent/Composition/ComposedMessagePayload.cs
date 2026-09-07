namespace Agent.Composition;

// Every member is optional on the wire: the composer itself decides which absences are a
// failure (body and cta_type), so a model response missing a key must still deserialize.
internal sealed record ComposedMessagePayload(
    string? Subject = null,
    string? Body = null,
    string? CtaType = null,
    IReadOnlyList<string>? CtaOptions = null,
    Uri? CtaLink = null);
