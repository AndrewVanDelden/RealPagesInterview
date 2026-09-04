namespace Agent.Domain;

public static class ConsentPreferencesExtensions
{
    public static bool IsOptedIn(this ConsentPreferences consent, CommunicationChannel channel) => channel switch
    {
        CommunicationChannel.Sms => consent.SmsOptIn,
        CommunicationChannel.Email => consent.EmailOptIn,
        CommunicationChannel.Voice => consent.VoiceOptIn,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown communication channel."),
    };
}
