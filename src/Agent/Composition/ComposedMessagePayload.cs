namespace Agent.Composition;

// Every member is optional on the wire: the composer itself decides which absences are a
// failure (body and cta_type), so a model response missing a key must still deserialize.
// There is no link member. The link is code-owned (A21, S2), so the schema never offers the
// model the field and a hallucinated host cannot reach an email.
internal sealed record ComposedMessagePayload(
    string? Subject = null,
    string? Body = null,
    string? CtaType = null,
    IReadOnlyList<string>? CtaOptions = null);
