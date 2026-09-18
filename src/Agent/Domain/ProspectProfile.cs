using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace Agent.Domain;

public sealed record ProspectProfile(
    string? FirstName = null,
    string? CityInterest = null,
    IReadOnlyList<string>? AmenityInterest = null,
    decimal? BudgetMax = null,
    int? TenureMonths = null,
    string? LoyaltyStatus = null,
    IReadOnlyList<string>? FeaturesEnablement = null,
    int? Age = null,
    DateTimeOffset? OptOutRequestedAt = null) : HasUnknownMembers
{
    // The categories trimmed from the ends of a greeting name: separators, control and format
    // characters, punctuation, currency, other and modifier symbols (where emoji are), marks with no
    // letter before them, and unassigned or private code points. Letters and digits are kept, and so
    // is anything between them, so "O'Neil" and "Mary-Jane" survive whole. Math symbols are kept:
    // angle brackets are one, and cleaning markup is a rule of its own.
    private static readonly FrozenSet<UnicodeCategory> TrimmedCategories = new[]
    {
        UnicodeCategory.SpaceSeparator, UnicodeCategory.LineSeparator, UnicodeCategory.ParagraphSeparator,
        UnicodeCategory.Control, UnicodeCategory.Format, UnicodeCategory.OtherSymbol, UnicodeCategory.ModifierSymbol,
        UnicodeCategory.NonSpacingMark, UnicodeCategory.SpacingCombiningMark, UnicodeCategory.EnclosingMark,
        UnicodeCategory.PrivateUse, UnicodeCategory.OtherNotAssigned,
        UnicodeCategory.ConnectorPunctuation, UnicodeCategory.DashPunctuation, UnicodeCategory.OpenPunctuation,
        UnicodeCategory.ClosePunctuation, UnicodeCategory.InitialQuotePunctuation, UnicodeCategory.FinalQuotePunctuation,
        UnicodeCategory.OtherPunctuation, UnicodeCategory.CurrencySymbol,
    }.ToFrozenSet();

    // Normalized, always-non-null views of the two interest fields, used by every
    // composer that describes stated interest - the null/empty-vs-non-empty guard lives
    // here once instead of being duplicated at each call site.
    public IReadOnlyList<string> Amenities => AmenityInterest is { Count: > 0 } amenities ? amenities : [];

    public string City => CityInterest is { Length: > 0 } city ? city : string.Empty;

    // The name a message greets: the first name without the whitespace, emoji, symbols and invisible
    // characters around it, or null when nothing is left, which is also what an absent or blank first
    // name is. Every reader of the name for a greeting or a check reads this, so both composers, the
    // model's prompt, the ingest notes and the scorer agree on who is greeted. A method, not a property:
    // the reader binds a property's name even when it is ignored, so an input member called
    // greeting_name would be swallowed rather than listed as undeclared.
    public string? GreetingName() => TrimSurroundingSymbols(FirstName);

    // Trimmed by whole grapheme cluster (UAX 29), so an emoji sequence joined by U+200D goes as one
    // and a letter followed by a combining accent stays whole. A cluster is trimmed when its first
    // code point is in TrimmedCategories. Markup is left for a rule of its own: angle brackets are
    // punctuation and math symbols, and cleaning them is not this rule. O(n) in the name's length.
    private static string? TrimSurroundingSymbols(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var clusters = new List<string>();
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(name);
        while (elements.MoveNext())
        {
            clusters.Add(elements.GetTextElement());
        }

        int start = clusters.FindIndex(IsKept);
        if (start < 0)
        {
            return null;
        }

        int end = clusters.FindLastIndex(IsKept);
        return WithoutTrailingJoiners(string.Concat(clusters.Skip(start).Take(end - start + 1)));
    }

    // A code unit that does not decode, such as a lone surrogate, decodes as U+FFFD, an other symbol,
    // so it is trimmed rather than throwing on text the reader accepted.
    // A keycap emoji starts with a digit or "#", so it is recognized by the combining keycap it carries.
    private static bool IsKept(string cluster)
    {
        Rune.DecodeFromUtf16(cluster, out Rune first, out _);
        return !TrimmedCategories.Contains(Rune.GetUnicodeCategory(first)) && !cluster.Contains('\u20E3', StringComparison.Ordinal);
    }

    // A joiner or variation selector after the last letter belongs to that letter's cluster, so the
    // cluster trim keeps it; it is invisible and joins nothing, so it goes too. A combining accent is a
    // mark, not a format character or a variation selector, and stays. The kept cluster the name
    // starts with ends the loop before the name is empty.
    private static string WithoutTrailingJoiners(string name)
    {
        string trimmed = name;
        while (IsTrailingJoiner(trimmed, out int width))
        {
            trimmed = trimmed[..^width];
        }

        return trimmed;
    }

    private static bool IsTrailingJoiner(string text, out int width)
    {
        Rune.DecodeLastFromUtf16(text, out Rune last, out width);
        return Rune.GetUnicodeCategory(last) == UnicodeCategory.Format || last.Value is >= 0xFE00 and <= 0xFE0F;
    }
}
