namespace Agent.Domain;

public sealed record ConsentPreferences(bool EmailOptIn, bool SmsOptIn, bool VoiceOptIn);
