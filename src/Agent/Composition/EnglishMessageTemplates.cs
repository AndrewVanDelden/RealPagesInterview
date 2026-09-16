using System.Collections.Frozen;

namespace Agent.Composition;

// The English prose of the offline composer (A12, A13). Sample 1's own spellings where it
// has one: the numbered options read "Reply 1 for ..., 2 for ..." and the opt-out is the
// whole word STOP, which is what OptOutInstructions looks for on both channels. The options
// for reschedule and intent_capture are the hold-out labels' own: today and tomorrow in
// prospect_no_show_reengage, yes, no and details in resident_renewal_undecided_followup. A tour
// invitation has no row: its options are the planned tour slots, written as dates in every language.
internal static class EnglishMessageTemplates
{
    public static readonly MessageTemplates Set = new(
        LanguageTag: "en",
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
        // The renewal offer's terms in the hold-out label's own words, resident_renewal_90day_notice.
        RenewalPriceHoldSentence: "We've reserved current pricing for {0} days.",
        RenewalTextRemindersSentence: "If you prefer text, reply YES to get reminders by SMS.",
        CtaPhraseByType: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["schedule_tour"] = "book a tour",
            ["reply"] = "learn more",
            ["reschedule"] = "reschedule your tour",
            ["intent_capture"] = "tell us if you plan to renew",
            ["review_renewal_details"] = "review your renewal details",
            ["get_started"] = "get started with your move-in checklist",
            ["enroll_loyalty"] = "enroll in the loyalty program",
            ["review_renewal"] = "review your renewal offer",
        }.ToFrozenDictionary(StringComparer.Ordinal),
        SmsOptionsByCtaType: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["reschedule"] = ["today", "tomorrow"],
            ["intent_capture"] = ["yes", "no", "details"],

            // synthetic_v2 v2_resident_move_in: the two key pickup times.
            ["confirm_move_in"] = ["9 AM", "1 PM"],
        }.ToFrozenDictionary(StringComparer.Ordinal),
        GenericSmsOptions: ["a question", "a tour"],
        GenericSmsOptionsWithoutTour: ["a question"]);
}
