using System.Text.Json.Serialization;

namespace Agent.Domain;

public sealed record ProspectCase(
    string TaskId,
    string? Persona,
    string? LifecycleStage,
    ConsentPreferences? Consent,
    IReadOnlyList<CommunicationChannel> ChannelPreferences,
    ProspectContext? Input,
    CaseAssertions? Assertions,
    CaseThresholds? Thresholds,
    [property: JsonConverter(typeof(LenientExpectedOutcomeConverter))] ExpectedOutcome? Expected) : HasUnknownMembers
{
    // A17: System.Text.Json binds through this constructor, so exactly these two members are
    // required (RespectRequiredConstructorParameters on AgentJsonOptions), and a line missing one
    // is an error row naming it. Every other member binds through its init setter when present and
    // is null when absent, since the input can carry cases the sample files do not show and a list
    // fitted to them would refuse those records. Consent is one of those: a record that states no
    // consent object is read and answered do not contact, where refusing the line wrote no decision
    // for that person at all.
    [JsonConstructor]
    public ProspectCase(string taskId, IReadOnlyList<CommunicationChannel> channelPreferences)
        : this(taskId, null, null, null, channelPreferences, null, null, null, null)
    {
    }

    // An absent consent object is consent to nothing: every opt-in is absent, and only an explicit
    // true opts a channel in (ConsentPreferences). Every decision reads consent through this.
    public ConsentPreferences ConsentOrEmpty => Consent ?? new ConsentPreferences();

    // An absent object states nothing; every member of these records is optional, so
    // an empty instance is the honest view for a consumer.
    public ProspectContext ContextOrEmpty => Input ?? new ProspectContext();

    public CaseConstraints ConstraintsOrEmpty => Assertions?.Constraints ?? new CaseConstraints();

    public CaseThresholds ThresholdsOrEmpty => Thresholds ?? new CaseThresholds();
}
