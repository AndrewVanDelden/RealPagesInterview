using System.Collections.Frozen;

namespace Agent.Composition;

// The English prose of the offline composer (A12, A13). Sample 1's own spellings where it
// has one: the numbered options read "Reply 1 for Thu, 2 for Fri." and the opt-out is the
// whole word STOP, which is what OptOutInstructions looks for on both channels.
internal static class EnglishMessageTemplates
{
    public static readonly MessageTemplates Set = new(
        GreetingWithName: "Hi {0}",
        GreetingWithoutName: "Hi",
        SmsWelcome: " Welcome to {0}.",
        InterestAmenities: "interested in {0}",
        InterestCity: "looking in {0}",
        InterestSentence: "We heard you're {0}. ",
        Conjunction: " and ",
        SmsCtaSentence: "Reply to {0}.",
        SmsOptionsSentence: "Reply {0}.",
        SmsOption: "{0} for {1}",
        SmsOptOut: "Reply STOP to opt out.",
        EmailCtaSentence: "Reply or click to {0}",
        EmailAtProperty: " at {0}",
        EmailLinkLine: "Get started: {0}",
        EmailOptOut: "To opt out of emails, reply STOP.",
        EmailSubjectForProperty: "Tour {0}",
        EmailSubjectGeneric: "Your next step",
        CtaPhraseByType: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["schedule_tour"] = "book a tour",
            ["reply"] = "learn more",
        }.ToFrozenDictionary(StringComparer.Ordinal),
        SmsOptionsByCtaType: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["schedule_tour"] = ["Thu", "Fri"],
        }.ToFrozenDictionary(StringComparer.Ordinal),
        GenericSmsOptions: ["a question", "a tour"]);
}
