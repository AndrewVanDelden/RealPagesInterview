using Agent.Common;
using Agent.Decisions;
using Agent.Domain;
using Xunit;

namespace Agent.Tests.Decisions;

public class ChannelSelectorTests
{
    private static readonly ChannelSelector Selector = new();

    [Fact]
    public void Select_Sample1Preferences_ReturnsSms()
    {
        var consent = new ConsentPreferences(EmailOptIn: true, SmsOptIn: true, VoiceOptIn: false);
        CommunicationChannel[] preferences = [CommunicationChannel.Sms, CommunicationChannel.Email];

        Option<CommunicationChannel> selected = Selector.Select(preferences, consent);

        Assert.True(selected.HasValue);
        Assert.Equal(CommunicationChannel.Sms, selected.Value);
    }

    [Fact]
    public void Select_FirstPreferenceNotConsented_FallsBackToSecondPreference()
    {
        var consent = new ConsentPreferences(EmailOptIn: true, SmsOptIn: false, VoiceOptIn: false);
        CommunicationChannel[] preferences = [CommunicationChannel.Sms, CommunicationChannel.Email];

        Option<CommunicationChannel> selected = Selector.Select(preferences, consent);

        Assert.True(selected.HasValue);
        Assert.Equal(CommunicationChannel.Email, selected.Value);
    }

    [Fact]
    public void Select_NoneConsented_ReturnsNone()
    {
        var consent = new ConsentPreferences(EmailOptIn: false, SmsOptIn: false, VoiceOptIn: false);
        CommunicationChannel[] preferences = [CommunicationChannel.Sms, CommunicationChannel.Email];

        Option<CommunicationChannel> selected = Selector.Select(preferences, consent);

        Assert.False(selected.HasValue);
    }

    // Consent is the permission and a preference list is only an order, so when no preferred channel
    // is consented a consented channel outside the list is used, email before sms before voice:
    // synthetic_v2's record with no preferences and its record that prefers only an unconsented sms are
    // both labeled email.
    [Theory]
    [InlineData(true, true, true, new CommunicationChannel[0], CommunicationChannel.Email)]
    [InlineData(true, false, false, new[] { CommunicationChannel.Sms }, CommunicationChannel.Email)]
    [InlineData(false, true, true, new CommunicationChannel[0], CommunicationChannel.Sms)]
    [InlineData(false, false, true, new[] { CommunicationChannel.Sms }, CommunicationChannel.Voice)]
    [InlineData(true, true, false, new[] { CommunicationChannel.Voice }, CommunicationChannel.Email)]
    public void Select_NoPreferredChannelConsented_FallsBackToAConsentedChannelEmailFirst(bool email, bool sms, bool voice, CommunicationChannel[] preferences, CommunicationChannel expected)
    {
        var consent = new ConsentPreferences(EmailOptIn: email, SmsOptIn: sms, VoiceOptIn: voice);

        Option<CommunicationChannel> selected = Selector.Select(preferences, consent);

        Assert.True(selected.HasValue);
        Assert.Equal(expected, selected.Value);
    }
}
