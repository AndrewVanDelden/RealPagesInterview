using System.Text.RegularExpressions;

namespace Agent.Safety;

// D41: the term proxies match a copy of the text, never the text itself. The probe of
// 2026-09-09 ran 37 inputs through the patterns as written; three of the misses it found
// need no adversary at all, and this is what removes them:
//
//   families-only    a model composer writes the hyphen as ordinary prose
//   families  only   two spaces between the words of a two-word term
//   famil<ZWSP>ies   a format character that arrives by copy and paste
//
// The digit patterns deliberately do not use this: a Social Security number and a card
// number are matched on the raw text, where their own separators are already part of the
// pattern.
internal static partial class SafetyTextNormalizer
{
    // O(n) in the text length: one strip pass, one fold pass, one whitespace pass.
    internal static string NormalizeForTermMatching(string text)
    {
        string folded = FoldHyphens(FormatCharacters().Replace(text, string.Empty));

        return WhitespaceRun().Replace(folded.Replace('-', ' '), " ");
    }

    // U+2010 hyphen, U+2011 non-breaking hyphen, U+2013 en dash, U+2014 em dash. The one
    // copy of this fold: OptOutInstructions calls it rather than repeating it.
    // O(n) in the text length.
    internal static string FoldHyphens(string text) =>
        text.Replace('\u2010', '-').Replace('\u2011', '-').Replace('\u2013', '-').Replace('\u2014', '-');

    // U+200B zero-width space, U+200C zero-width non-joiner, U+200D zero-width joiner,
    // U+FEFF zero-width no-break space.
    [GeneratedRegex("[\u200B\u200C\u200D\uFEFF]")]
    private static partial Regex FormatCharacters();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
