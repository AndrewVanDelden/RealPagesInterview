namespace Agent.Common;

// One split-and-lowercase rule for a BCP 47 tag's primary subtag ("en", "en-US" and
// "es_MX" all resolve to "en"/"es"), shared so a caller matching a tag against known
// languages does not restate the split (LanguageDetector.TryParseTag, MessageTemplateCatalog.Resolve).
public static class Bcp47
{
    // O(1): one split, taking the first segment.
    public static string PrimarySubtag(string? tag) => tag?.Split('-', '_')[0].ToLowerInvariant() ?? string.Empty;
}
