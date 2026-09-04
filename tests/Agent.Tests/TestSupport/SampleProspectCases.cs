using Agent.Domain;

namespace Agent.Tests.TestSupport;

internal static class SampleProspectCases
{
    public static ProspectCase Minimal(
        string firstName = "Taylor",
        string propertyName = "Oak Ridge Apartments",
        string? cityInterest = "Richardson, TX",
        IReadOnlyList<string>? amenityInterest = null,
        string? primaryCta = "book_tour",
        bool includeOptOutInstructions = true)
    {
        var profile = new ProspectProfile(firstName, cityInterest, amenityInterest);
        var context = new ProspectContext(
            propertyName,
            new DateOnly(2026, 1, 10),
            DateTimeOffset.Parse("2025-12-08T15:04:00Z"),
            "America/Chicago",
            "en",
            profile);
        var consent = new ConsentPreferences(EmailOptIn: true, SmsOptIn: true, VoiceOptIn: false);
        var constraints = new CaseConstraints(NoPiiLeak: true, NoSensitiveDiscrimination: null, includeOptOutInstructions, primaryCta);
        var assertions = new CaseAssertions(RequiredStates: [], constraints);
        var thresholds = new CaseThresholds(2000, 0.85, 0.9, 0);

        return new ProspectCase(
            "test_case",
            "prospect",
            "new",
            consent,
            [CommunicationChannel.Sms, CommunicationChannel.Email],
            context,
            assertions,
            thresholds,
            Expected: null);
    }
}
