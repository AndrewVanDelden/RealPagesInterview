namespace Agent.Domain;

// Body is nullable because the oracle's suppressed shape is a next_message object with
// channel "none" and every other field null, so the output always carries the object. A
// message the agent composes always carries a body; consumers treat a null body as empty text.
public sealed record NextMessage(
    CommunicationChannel? Channel = null,
    DateTimeOffset? SendAt = null,
    string? Subject = null,
    string? Body = null,
    Cta? Cta = null);
