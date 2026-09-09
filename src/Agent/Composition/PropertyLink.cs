using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Agent.Common;

namespace Agent.Composition;

// A21: the email link is https://{slug}.example/{path}. The slug is the property name
// lowercased with a trailing property-type word dropped and every non-alphanumeric character
// removed, which is what turns sample 2's "Oak Ridge Apartments" into "oakridge". A record
// with no property name has no host, so it gets no link rather than an invented one.
internal static partial class PropertyLink
{
    private static readonly FrozenSet<string> TrailingTypeWords = new[]
    {
        "apartment",
        "apartments",
        "flats",
        "homes",
        "lofts",
        "place",
        "residences",
        "towers",
        "villas",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // O(n) in the length of the property name: one split, one set lookup, one substitution.
    public static Uri? For(string? propertyName, string path)
    {
        if (Presence.IsAbsent(propertyName))
        {
            return null;
        }

        string slug = Slug(propertyName!);

        return slug.Length == 0 ? null : new Uri($"https://{slug}.example/{path}");
    }

    // The type word is dropped only when something else remains: a property called "Lofts"
    // is its own slug, and dropping the word would leave no host at all.
    private static string Slug(string propertyName)
    {
        string[] words = propertyName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length > 1 && TrailingTypeWords.Contains(words[^1]))
        {
            words = words[..^1];
        }

        return NonSlugCharacters().Replace(string.Concat(words), string.Empty).ToLowerInvariant();
    }

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NonSlugCharacters();
}
