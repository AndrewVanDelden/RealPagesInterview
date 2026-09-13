namespace Agent.Domain;

public enum CommunicationChannel
{
    // The absence of a channel: the oracle's spelling of a suppressed message is a
    // next_message object with channel "none", so the output always carries both members.
    // First so that the enum's default value is the absence, never a real channel.
    None,
    Sms,
    Email,
    Voice,
    // A channel name the file used that this program does not know (A3). Kept as a real
    // value so the record parses; the selector skips it and the ingest notes count it.
    Unknown,
}
