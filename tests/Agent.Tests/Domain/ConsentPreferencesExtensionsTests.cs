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
}
