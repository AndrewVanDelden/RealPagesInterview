using Agent.Domain;

namespace Agent.Decisions;

public sealed class ConsentGate : IConsentGate
{
    public ConsentDecision Evaluate(ConsentPreferences consent, IReadOnlyList<CommunicationChannel> channelPreferences)
    {
        bool isContactable = channelPreferences.Any(consent.IsOptedIn);

        return new ConsentDecision(isContactable, ConsentVerified: true);
    }
}
