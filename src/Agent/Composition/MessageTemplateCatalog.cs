using System.Collections.Frozen;
using Agent.Common;

namespace Agent.Composition;

// D26: the languages the offline composer can serve, and nothing more. There is no
// allowlist here and none anywhere else: a record may state any language, the model path
// passes it through, and this table is only the list of languages this composer holds
// prose for. A tag with no set is served in English and reported (A13's
// locale_not_applied), which is a limit of the template file rather than a rule about
// which languages a prospect may use.
internal static class MessageTemplateCatalog
{
    private static readonly FrozenDictionary<string, MessageTemplates> ByPrimarySubtag =
        new Dictionary<string, MessageTemplates>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = EnglishMessageTemplates.Set,
            ["es"] = SpanishMessageTemplates.Set,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // O(1): one split and one hash lookup. The tag is matched on its primary subtag, so
    // "es", "es-MX" and "es_419" are one language (BCP 47). A13: an absent language is the
    // en default the record inherits, so nothing failed to be applied; a stated language
    // with no set is English with LocaleApplied false.
    public static (MessageTemplates Templates, bool LocaleApplied) Resolve(string? languageTag)
    {
        if (Presence.IsAbsent(languageTag))
        {
            return (EnglishMessageTemplates.Set, true);
        }

        string primarySubtag = languageTag!.Split('-', '_')[0];

        return ByPrimarySubtag.TryGetValue(primarySubtag, out MessageTemplates? templates)
            ? (templates, true)
            : (EnglishMessageTemplates.Set, false);
    }
}
