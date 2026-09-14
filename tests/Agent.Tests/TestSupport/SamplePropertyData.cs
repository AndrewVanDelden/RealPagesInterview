using Agent.Composition;

namespace Agent.Tests.TestSupport;

// One property's facts as the property management system would state them: tour availability,
// extended tour hours, a starting total monthly price with its as-of date, and one renewal offer
// for unit A-204.
internal static class SamplePropertyData
{
    public const string TourAvailability = "Tours are available this week.";

    public const string ExtendedTourHours = "Evening and weekend tours are available.";

    public static PropertyData OakRidge() =>
        new(
        [
            new PropertyFacts(
                "Oak Ridge Apartments",
                TourAvailability,
                [new StartingPrice("studio", 1650m, new DateOnly(2025, 12, 8))],
                [new RenewalOffer(Unit: "A‑204", OfferId: "REN‑A204‑2026", PriceHoldDays: 10, TextRemindersOffered: true)],
                ExtendedTourHours),
        ]);
}
