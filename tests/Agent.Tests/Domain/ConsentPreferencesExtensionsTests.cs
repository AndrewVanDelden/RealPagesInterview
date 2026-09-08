using Agent.Domain;
using Xunit;

namespace Agent.Tests.Domain;

public class ConsentPreferencesExtensionsTests
{
    [Fact]
    public void IsOptedIn_UnknownChannel_ThrowsArgumentOutOfRangeException()
    {
        var consent = new ConsentPreferences(EmailOptIn: true, SmsOptIn: true, VoiceOptIn: true);
        var unknownChannel = (CommunicationChannel)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => consent.IsOptedIn(unknownChannel));
    }

    // None is the absence of a channel, so nobody has opted in to it, even with every
    // real channel consented.
    [Fact]
    public void IsOptedIn_None_ReturnsFalse()
    {
        var consent = new ConsentPreferences(EmailOptIn: true, SmsOptIn: true, VoiceOptIn: true);

        Assert.False(consent.IsOptedIn(CommunicationChannel.None));
    }

    // A3: an unrecognized channel name in channel_preferences is skipped, never consented.
    [Fact]
    public void IsOptedIn_Unknown_ReturnsFalse()
    {
        var consent = new ConsentPreferences(EmailOptIn: true, SmsOptIn: true, VoiceOptIn: true);

        Assert.False(consent.IsOptedIn(CommunicationChannel.Unknown));
    }

    // D1: an absent opt-in member is not consent.
    [Fact]
    public void IsOptedIn_OptInMemberAbsent_ReturnsFalse()
    {
        var consent = new ConsentPreferences(EmailOptIn: null, SmsOptIn: true, VoiceOptIn: null);

        Assert.False(consent.IsOptedIn(CommunicationChannel.Email));
        Assert.True(consent.IsOptedIn(CommunicationChannel.Sms));
        Assert.False(consent.IsOptedIn(CommunicationChannel.Voice));
    }
}
