using System.Text.Json.Serialization;

namespace Agent.Domain;

public sealed record ProspectCase(
    string TaskId,
    string Persona,
    string LifecycleStage,
    ConsentPreferences Consent,
    IReadOnlyList<CommunicationChannel> ChannelPreferences,
    ProspectContext Input,
    CaseAssertions Assertions,
    CaseThresholds Thresholds,
    [property: JsonConverter(typeof(LenientExpectedOutcomeConverter))] ExpectedOutcome? Expected);
