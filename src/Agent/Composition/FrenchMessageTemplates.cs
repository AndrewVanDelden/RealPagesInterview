using System.Collections.Frozen;

namespace Agent.Composition;

// The French prose of both composers, for the Canadian properties the product serves. Written in the
// vous form a leasing office uses with a prospect or resident it has not met. The opt-out keeps the
// whole word STOP, as the Spanish set does: OptOutInstructions is one definition for the validator
// and the scorer, and STOP is the keyword a Canadian short code answers in English beside ARRET.
internal static class FrenchMessageTemplates
{
    public static readonly MessageTemplates Set = new(
        LanguageTag: "fr",
        GreetingWithName: "Bonjour {0}",
        GreetingWithoutName: "Bonjour",
        SmsWelcome: " Bienvenue à {0}.",
        InterestAmenities: "vous vous intéressez à {0}",
        InterestCity: "vous cherchez à {0}",
        InterestSentence: "Vous nous avez dit que {0}. ",
        Conjunction: " et ",
        SmsCtaSentence: "Répondez pour {0}.",
        SmsOptionsSentence: "Répondez {0}.",
        SmsOption: "{0} pour {1}",
        SmsOptOut: "Répondez STOP pour vous désabonner.",
        EmailCtaSentence: "Répondez ou cliquez pour {0}",
        EmailAtProperty: " à {0}",
        EmailLinkLine: "Commencez ici : {0}",
        EmailOptOut: "Pour ne plus recevoir nos courriels, répondez STOP.",
        EmailSubjectForProperty: "Visitez {0}",
        EmailSubjectGeneric: "Votre prochaine étape",
        RenewalPriceHoldSentence: "Nous réservons le prix actuel pendant {0} jours.",
        RenewalTextRemindersSentence: "Si vous préférez les messages texte, répondez OUI pour recevoir des rappels par SMS.",
        CtaPhraseByType: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["schedule_tour"] = "planifier une visite",
            ["reply"] = "en savoir plus",
            ["reschedule"] = "reprogrammer votre visite",
            ["intent_capture"] = "nous dire si vous comptez renouveler",
            ["review_renewal_details"] = "consulter les détails de votre renouvellement",
            ["get_started"] = "préparer votre emménagement",
            ["enroll_loyalty"] = "vous inscrire au programme de fidélité",
            ["review_renewal"] = "consulter votre offre de renouvellement",
        }.ToFrozenDictionary(StringComparer.Ordinal),
        SmsOptionsByCtaType: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["reschedule"] = ["aujourd'hui", "demain"],
            ["intent_capture"] = ["oui", "non", "détails"],
        }.ToFrozenDictionary(StringComparer.Ordinal),
        GenericSmsOptions: ["une question", "une visite"],
        GenericSmsOptionsWithoutTour: ["une question"]);
}
