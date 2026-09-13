using Agent.Common;

namespace Agent.Evaluation;

// The strongest checkable proxy for "body language equals input.language" without
// a model or a dependency: a stop-word count per language over the languages the sets
// contain. The two lists are disjoint function words; content words are left out so a
// property name or an amenity never votes.
public static class LanguageDetector
{
    private static readonly HashSet<string> EnglishStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "to", "you", "your", "for", "this", "and", "we", "is", "are", "of", "it", "or",
        "here", "with", "would", "please", "before", "welcome", "reply", "opt", "out", "our",
        "at", "on", "from", "can", "want", "time", "week", "now",
    };

    private static readonly HashSet<string> SpanishStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "el", "la", "los", "las", "para", "por", "tu", "tus", "en", "una", "un", "esta", "este",
        "de", "que", "y", "con", "responde", "gracias", "hola", "quieres", "aquí", "semana",
        "nuestro", "nuestra", "puedes", "cancelar", "ahora", "no", "se", "su", "sus", "al",
        "del", "mi", "mis", "más", "pero", "si", "también",
    };

    // O(n) in the text length: one tokenizing pass, two set lookups per distinct word.
    public static MessageLanguage? Detect(string text) => Detect(MessageWords.Of(text));

    // O(n) in the word count: two set lookups per distinct word. Takes an already
    // tokenized set so a caller scoring more than one thing about the same text (Evaluator
    // also checks personalization coverage) tokenizes it once, not once per check.
    // Null when neither language leads: no stop words at all, or an exact tie.
    internal static MessageLanguage? Detect(IReadOnlySet<string> words)
    {
        int english = 0;
        int spanish = 0;

        foreach (string word in words)
        {
            if (EnglishStopWords.Contains(word))
            {
                english++;
            }

            if (SpanishStopWords.Contains(word))
            {
                spanish++;
            }
        }

        if (english == spanish)
        {
            return null;
        }

        return english > spanish ? MessageLanguage.English : MessageLanguage.Spanish;
    }

    // The primary subtag of a BCP 47 tag ("en", "en-US", "es_MX"); false for a language
    // the detector does not know, so the caller reports it as not measured.
    public static bool TryParseTag(string? tag, out MessageLanguage language)
    {
        switch (Bcp47.PrimarySubtag(tag))
        {
            case "en":
                language = MessageLanguage.English;
                return true;
            case "es":
                language = MessageLanguage.Spanish;
                return true;
            default:
                language = default;
                return false;
        }
    }
}
