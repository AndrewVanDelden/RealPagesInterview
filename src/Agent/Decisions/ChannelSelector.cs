using Agent.Common;
using Agent.Domain;

namespace Agent.Decisions;

public sealed class ChannelSelector : IChannelSelector
{
    public Option<CommunicationChannel> Select(IReadOnlyList<CommunicationChannel> channelPreferences, ConsentPreferences consent)
    {
        foreach (CommunicationChannel channel in channelPreferences)
        {
            if (consent.IsOptedIn(channel))
            {
                return Option<CommunicationChannel>.Some(channel);
            }
        }

        return Option<CommunicationChannel>.None();
    }
}
