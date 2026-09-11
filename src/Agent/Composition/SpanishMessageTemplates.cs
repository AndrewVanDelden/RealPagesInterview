using System.Collections.Frozen;

namespace Agent.Composition;

// The Spanish prose of the offline composer (A13), earned by the synthetic set's
// item 4 record. The greeting and the welcome are written without a gendered adjective,
// because no input field states the prospect's gender and "bienvenido" or "bienvenida"
// would be a guess. The opt-out keeps the whole word STOP: OptOutInstructions is one
// definition for the validator and the scorer, and it is not translated.
internal static class SpanishMessageTemplates
{
    public static readonly MessageTemplates Set = new(
        GreetingWithName: "Hola {0}",
        GreetingWithoutName: "Hola",
        SmsWelcome: " Te damos la bienvenida a {0}.",
        InterestAmenities: "te interesa {0}",
        InterestCity: "buscas en {0}",
        InterestSentence: "Nos dijiste que {0}. ",
        Conjunction: " y ",
        SmsCtaSentence: "Responde para {0}.",
        SmsOptionsSentence: "Responde {0}.",
        SmsOption: "{0} para {1}",
        SmsOptOut: "Responde STOP para cancelar.",
        EmailCtaSentence: "Responde o haz clic para {0}",
        EmailAtProperty: " en {0}",
        EmailLinkLine: "Empieza aquí: {0}",
        EmailOptOut: "Para cancelar los correos, responde STOP.",
        EmailSubjectForProperty: "Visita {0}",
        EmailSubjectGeneric: "Tu siguiente paso",
        CtaPhraseByType: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["schedule_tour"] = "agendar una visita",
            ["reply"] = "saber más",
            ["reschedule"] = "reprogramar tu visita",
            ["intent_capture"] = "decirnos si piensas renovar",
            ["review_renewal_details"] = "revisar los detalles de tu renovación",
        }.ToFrozenDictionary(StringComparer.Ordinal),
        SmsOptionsByCtaType: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["schedule_tour"] = ["jueves", "viernes"],
            ["reschedule"] = ["hoy", "mañana"],
            ["intent_capture"] = ["sí", "no", "detalles"],
        }.ToFrozenDictionary(StringComparer.Ordinal),
        GenericSmsOptions: ["una pregunta", "una visita"]);
}
