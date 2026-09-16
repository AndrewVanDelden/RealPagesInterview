using System.Collections.Frozen;
using Agent.Common;

namespace Agent.Composition;

// The languages a message can be written in: the ones the program holds a full set for, since the
// options and opt-out sentences are code's and must be in the message's own language. A record may
// state any tag; it is resolved here once, for both composers, and a tag with no set is served
// wholly in English and reported (A13's locale_not_applied), so a message is always in one language.
internal static class MessageTemplateCatalog
{
    private static readonly FrozenDictionary<string, MessageTemplates> ByPrimarySubtag =
        new Dictionary<string, MessageTemplates>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = EnglishMessageTemplates.Set,
            ["es"] = SpanishMessageTemplates.Set,
            ["fr"] = FrenchMessageTemplates.Set,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // O(1): one split and one hash lookup. The tag is matched on its primary subtag, so "es", "es-MX"
    // and "es_419" are one language: RFC 4647 lookup truncated to the primary subtag, the only level
    // the sets are kept at, then the English default. A13: an absent language is the
    // en default the record inherits, so nothing failed to be applied; a stated language
    // with no set is English with LocaleApplied false.
    public static (MessageTemplates Templates, bool LocaleApplied) Resolve(string? languageTag)
    {
        if (Presence.IsAbsent(languageTag))
        {
            return (EnglishMessageTemplates.Set, true);
        }

        string primarySubtag = Bcp47.PrimarySubtag(languageTag);

        return ByPrimarySubtag.TryGetValue(primarySubtag, out MessageTemplates? templates)
            ? (templates, true)
            : (EnglishMessageTemplates.Set, false);
    }
}
