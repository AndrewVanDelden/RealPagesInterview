namespace Agent.Domain;

public static class ConsentPreferencesExtensions
{
    // None is the absence of a channel and Unknown is a name the program does not know:
    // nobody has opted in to either. An absent opt-in member is not consent (D1).
    public static bool IsOptedIn(this ConsentPreferences consent, CommunicationChannel channel) => channel switch
    {
        CommunicationChannel.None => false,
        CommunicationChannel.Unknown => false,
        CommunicationChannel.Sms => consent.SmsOptIn == true,
        CommunicationChannel.Email => consent.EmailOptIn == true,
        CommunicationChannel.Voice => consent.VoiceOptIn == true,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown communication channel."),
    };
}
