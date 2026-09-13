namespace Agent.Domain;

// The object is required (A17); each opt-in inside it is optional, and an absent opt-in
// is not consent: only an explicit true opts a channel in.
public sealed record ConsentPreferences(bool? EmailOptIn = null, bool? SmsOptIn = null, bool? VoiceOptIn = null) : HasUnknownMembers;
