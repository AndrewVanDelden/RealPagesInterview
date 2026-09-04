namespace Agent.Domain;

public sealed record NextMessage(
    CommunicationChannel? Channel,
    DateTimeOffset? SendAt,
    string? Subject,
    string Body,
    Cta? Cta);
