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

    // Contactable and which channel are one question: no selected channel is the answer to both,
    // so the case a separate consent gate once proved lives here. Consent on every channel is not
    // a channel: a record that states no preference has nothing to send on, so the answer is none.
    [Fact]
    public void Select_EmptyChannelPreferencesDespiteFullConsent_ReturnsNone()
    {
        var consent = new ConsentPreferences(EmailOptIn: true, SmsOptIn: true, VoiceOptIn: true);
        CommunicationChannel[] preferences = [];

        Option<CommunicationChannel> selected = Selector.Select(preferences, consent);

        Assert.False(selected.HasValue);
    }
}
