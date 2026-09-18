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
        LanguageTag: "es",
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
        VoiceCallerOpening: "Te llama {0}.",
        VoiceCtaSentence: "Te llamamos para ayudarte a {0}.",
        VoiceOptionsSentence: "Marca {0}.",
        VoiceOptOut: "Para no recibir más llamadas, marca 9.",
        EmailCtaSentence: "Responde o haz clic para {0}",
        EmailAtProperty: " en {0}",
        EmailLinkLine: "Empieza aquí: {0}",
        EmailOptOut: "Para cancelar los correos, responde STOP.",
        EmailSubjectForProperty: "Visita {0}",
        EmailSubjectGeneric: "Tu siguiente paso",
        RenewalPriceHoldSentence: "Reservamos el precio actual por {0} días.",
        RenewalTextRemindersSentence: "Si prefieres mensajes de texto, responde SÍ para recibir recordatorios por SMS.",
        CtaPhraseByType: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["schedule_tour"] = "agendar una visita",
            ["reply"] = "saber más",
            ["reschedule"] = "reprogramar tu visita",
            ["intent_capture"] = "decirnos si piensas renovar",
            ["review_renewal_details"] = "revisar los detalles de tu renovación",
            ["get_started"] = "completar tu proceso de mudanza",
            ["enroll_loyalty"] = "inscribirte en el programa de lealtad",
            ["review_renewal"] = "revisar tu oferta de renovación",
        }.ToFrozenDictionary(StringComparer.Ordinal),
        SmsOptionsByCtaType: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["reschedule"] = ["hoy", "mañana"],
            ["intent_capture"] = ["sí", "no", "detalles"],
        }.ToFrozenDictionary(StringComparer.Ordinal),
        GenericSmsOptions: ["una pregunta", "una visita"],
        GenericSmsOptionsWithoutTour: ["una pregunta"]);
}
