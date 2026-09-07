namespace Agent.Domain;

public enum CommunicationChannel
{
    // The absence of a channel: the oracle's spelling of a suppressed message is a
    // next_message object with channel "none" (retrospective D3). First so that the enum's
    // default value is the absence, never a real channel.
    None,
    Sms,
    Email,
    Voice,
}
