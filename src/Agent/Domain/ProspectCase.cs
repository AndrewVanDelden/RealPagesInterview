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
    // A17: System.Text.Json binds through this constructor, so exactly these three members
    // are required (RespectRequiredConstructorParameters on AgentJsonOptions), and a line
    // missing one is an error row naming it. Every other member binds through its init setter
    // when present and is null when absent, since the input can carry cases the sample files
    // do not show and a list fitted to them would refuse those records.
    [JsonConstructor]
    public ProspectCase(string taskId, ConsentPreferences consent, IReadOnlyList<CommunicationChannel> channelPreferences)
        : this(taskId, null, null, consent, channelPreferences, null, null, null, null)
    {
    }

    // An absent object states nothing; every member of these records is optional, so
    // an empty instance is the honest view for a consumer.
    public ProspectContext ContextOrEmpty => Input ?? new ProspectContext();

    public CaseConstraints ConstraintsOrEmpty => Assertions?.Constraints ?? new CaseConstraints();

    public CaseThresholds ThresholdsOrEmpty => Thresholds ?? new CaseThresholds();
}
