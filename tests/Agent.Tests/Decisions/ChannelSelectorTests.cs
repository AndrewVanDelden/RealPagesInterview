using Agent.Common;
using Agent.Decisions;
using Agent.Domain;
using Xunit;

namespace Agent.Tests.Decisions;

public class ChannelSelectorTests
{
    private static readonly IChannelSelector Selector = new ChannelSelector();

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
    public void Select_Sample2Preferences_ReturnsEmail()
    {
        var consent = new ConsentPreferences(EmailOptIn: true, SmsOptIn: false, VoiceOptIn: false);
        CommunicationChannel[] preferences = [CommunicationChannel.Email, CommunicationChannel.Sms];

        Option<CommunicationChannel> selected = Selector.Select(preferences, consent);

        Assert.True(selected.HasValue);
        Assert.Equal(CommunicationChannel.Email, selected.Value);
    }

    [Fact]
    public void Select_VoiceConsentedAndFirstPreference_ReturnsVoice()
    {
        var consent = new ConsentPreferences(EmailOptIn: false, SmsOptIn: false, VoiceOptIn: true);
        CommunicationChannel[] preferences = [CommunicationChannel.Voice, CommunicationChannel.Sms];

        Option<CommunicationChannel> selected = Selector.Select(preferences, consent);

        Assert.True(selected.HasValue);
        Assert.Equal(CommunicationChannel.Voice, selected.Value);
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
}
