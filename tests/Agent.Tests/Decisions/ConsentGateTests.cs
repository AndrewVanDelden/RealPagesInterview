using Agent.Decisions;
using Agent.Domain;
using Xunit;

namespace Agent.Tests.Decisions;

public class ConsentGateTests
{
    private static readonly IConsentGate Gate = new ConsentGate();

    [Fact]
    public void Evaluate_AnyPreferredChannelOptedIn_ReturnsContactable()
    {
        var consent = new ConsentPreferences(EmailOptIn: true, SmsOptIn: true, VoiceOptIn: false);
        CommunicationChannel[] preferences = [CommunicationChannel.Sms, CommunicationChannel.Email];

        ConsentDecision decision = Gate.Evaluate(consent, preferences);

        Assert.True(decision.IsContactable);
        Assert.True(decision.ConsentVerified);
    }

    [Fact]
    public void Evaluate_NoPreferredChannelOptedIn_ReturnsSuppressed()
    {
        var consent = new ConsentPreferences(EmailOptIn: false, SmsOptIn: false, VoiceOptIn: false);
        CommunicationChannel[] preferences = [CommunicationChannel.Sms, CommunicationChannel.Email];

        ConsentDecision decision = Gate.Evaluate(consent, preferences);

        Assert.False(decision.IsContactable);
        Assert.True(decision.ConsentVerified);
    }
}
