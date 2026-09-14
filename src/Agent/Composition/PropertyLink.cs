using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Agent.Common;

namespace Agent.Composition;

// A21: the email link is https://{slug}.example/{path}. The slug is the property name lowercased
// with a trailing property-type word dropped and every non-alphanumeric character removed, which
// is what turns sample 2's "Oak Ridge Apartments" into "oakridge". A record with no property name
// has no host, so it gets no link rather than an invented one.
// A26: a path may name the record's unit. Its segment is the unit with every dash character
// written as a hyphen and every character other than an ASCII letter, digit or hyphen removed,
// which is what turns the hold-out's "A‑204", written with a non-breaking hyphen, into the
// labels' "A-204". A path that names the unit on a record with no usable unit gets no link, for
// the same reason an absent property name does.
internal static partial class PropertyLink
{
    public const string UnitSegment = "{unit}";

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

    // O(n) in the length of the property name and the unit: one split, one set lookup, and a
    // bounded number of substitutions.
    public static Uri? For(string? propertyName, string path, string? unit)
    {
        if (Presence.IsAbsent(propertyName))
        {
            return null;
        }

        string slug = Slug(propertyName!);

        if (slug.Length == 0)
        {
            return null;
        }

        if (!path.Contains(UnitSegment, StringComparison.Ordinal))
        {
            return new Uri($"https://{slug}.example/{path}");
        }

        string unitSegment = Presence.IsAbsent(unit)
            ? string.Empty
            : NonUnitCharacters().Replace(FoldDashes(unit!), string.Empty);

        return unitSegment.Length == 0
            ? null
            : new Uri($"https://{slug}.example/{path.Replace(UnitSegment, unitSegment, StringComparison.Ordinal)}");
    }

    // Every dash character written as an ASCII hyphen, so a unit is the same identifier
    // whichever one a person typed it with. The one place this fold is written: PropertyFacts
    // calls it too, to match a renewal offer's unit against a record's regardless of which
    // dash each was authored with.
    internal static string FoldDashes(string unit) => DashCharacters().Replace(unit, "-");

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

    [GeneratedRegex(@"\p{Pd}")]
    private static partial Regex DashCharacters();

    [GeneratedRegex("[^A-Za-z0-9-]")]
    private static partial Regex NonUnitCharacters();
}
