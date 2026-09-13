namespace Agent.Domain;

// Every constraint is optional, as is every record member except task_id, consent and
// channel_preferences; an absent one is not required, so a consumer tests for "== true",
// never for the bare value.
public sealed record CaseConstraints(
    bool? NoPiiLeak = null,
    bool? NoSensitiveDiscrimination = null,
    bool? IncludeOptOutInstructions = null,
    string? PrimaryCta = null) : HasUnknownMembers;
