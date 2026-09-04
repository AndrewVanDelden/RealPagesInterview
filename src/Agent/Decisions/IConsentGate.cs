using Agent.Domain;

namespace Agent.Decisions;

public interface IConsentGate
{
    ConsentDecision Evaluate(ConsentPreferences consent, IReadOnlyList<CommunicationChannel> channelPreferences);
}
