using System.Text.Json.Serialization;

namespace Agent.Domain;

public sealed record ProspectCase(
    string TaskId,
    string? Persona,
    string? LifecycleStage,
    ConsentPreferences Consent,
    IReadOnlyList<CommunicationChannel> ChannelPreferences,
    ProspectContext? Input,
    CaseAssertions? Assertions,
    CaseThresholds? Thresholds,
    [property: JsonConverter(typeof(LenientExpectedOutcomeConverter))] ExpectedOutcome? Expected) : HasUnknownMembers
{
    // D1 and A17: System.Text.Json binds through this constructor, so exactly these three
    // members are required (RespectRequiredConstructorParameters on AgentJsonOptions);
    // every other member binds through its init setter when present and is null when absent.
    [JsonConstructor]
    public ProspectCase(string taskId, ConsentPreferences consent, IReadOnlyList<CommunicationChannel> channelPreferences)
        : this(taskId, null, null, consent, channelPreferences, null, null, null, null)
    {
    }

    // An absent object states nothing; every member of these records is optional (D1), so
    // an empty instance is the honest view for a consumer.
    public ProspectContext ContextOrEmpty => Input ?? new ProspectContext();

    public CaseConstraints ConstraintsOrEmpty => Assertions?.Constraints ?? new CaseConstraints();

    public CaseThresholds ThresholdsOrEmpty => Thresholds ?? new CaseThresholds();
}
