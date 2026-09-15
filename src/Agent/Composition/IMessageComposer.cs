using Agent.Domain;

namespace Agent.Composition;

public interface IMessageComposer
{
    // tourSlots are the tour slots the agent planned for this record from its send time, the options a
    // tour invitation sms offers; null or empty when the caller planned none, and a tour sms then
    // offers the language set's generic pair rather than an invented day. referenceDate is the run's
    // reference date in the record's zone, which no composer can read from a clock; null when the
    // caller gives none.
    Task<ComposeOutcome> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<DateTimeOffset>? tourSlots = null,
        DateOnly? referenceDate = null);
}
