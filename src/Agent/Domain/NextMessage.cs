namespace Agent.Domain;

// Body is nullable because the oracle's suppressed shape is a next_message object with
// channel "none" and every other field null (retrospective D3). A message the agent
// composes always carries a body; consumers treat a null body as empty text.
public sealed record NextMessage(
    CommunicationChannel? Channel,
    DateTimeOffset? SendAt,
    string? Subject,
    string? Body,
    Cta? Cta);
