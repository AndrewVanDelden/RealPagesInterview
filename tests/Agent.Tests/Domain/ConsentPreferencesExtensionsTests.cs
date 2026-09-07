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
}
