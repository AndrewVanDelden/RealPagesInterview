using Agent.Common;
using Agent.Domain;

namespace Agent.Decisions;

// Consent is the permission and channel_preferences only the order to try channels in: the first
// preferred channel with consent is used, and when no preferred channel has consent a consented
// channel outside the list is, email before sms before voice, which is what synthetic_v2's record with
// no preferences and its record preferring only an unconsented sms are labeled. No consented channel
// at all is the one answer to "not contactable".
public sealed class ChannelSelector
{
    private static readonly CommunicationChannel[] FallbackOrder = [CommunicationChannel.Email, CommunicationChannel.Sms, CommunicationChannel.Voice];

    // O(p) in the preferences plus the three fallback channels.
    public Option<CommunicationChannel> Select(IReadOnlyList<CommunicationChannel> channelPreferences, ConsentPreferences consent)
    {
        foreach (CommunicationChannel channel in channelPreferences)
        {
            if (consent.IsOptedIn(channel))
            {
                return Option<CommunicationChannel>.Some(channel);
            }
        }

        foreach (CommunicationChannel channel in FallbackOrder)
        {
            if (consent.IsOptedIn(channel))
            {
                return Option<CommunicationChannel>.Some(channel);
            }
        }

        return Option<CommunicationChannel>.None();
    }
}
