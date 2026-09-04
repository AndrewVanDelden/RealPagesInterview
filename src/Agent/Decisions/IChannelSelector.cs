using Agent.Common;
using Agent.Domain;

namespace Agent.Decisions;

public interface IChannelSelector
{
    Option<CommunicationChannel> Select(IReadOnlyList<CommunicationChannel> channelPreferences, ConsentPreferences consent);
}
