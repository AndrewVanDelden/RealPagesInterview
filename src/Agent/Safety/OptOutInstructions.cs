using System.Text.RegularExpressions;

namespace Agent.Safety;

// D13 b: the one definition of "carries an opt-out instruction", shared by the validator
// (which enforces it) and the evaluator (which measures it), so the agent can never emit
// what the scorer rejects. The whole word STOP in capitals is the carrier keyword in every
// language the sets contain ("Reply STOP", "Responde STOP"); a lowercase "stop" is prose
// ("bus stop"). A lowercase "stop" only carries the instruction directly after "reply" or
// "text" ("Reply stop", "Text stop") - a composer not told to capitalize STOP still counts,
// while "Please stop by the leasing office" still does not. The unicode hyphens the labels
// use fold to a hyphen first, so "Opt-out" spelled with U+2011 counts.
public static partial class OptOutInstructions
{
    private static readonly string[] Phrases = ["opt out", "opt-out", "unsubscribe"];

    // O(n) in the text length.
    public static bool IsPresent(string text)
    {
        string folded = FoldHyphens(text);

        return StopKeyword().IsMatch(folded)
            || StopDirective().IsMatch(folded)
            || Phrases.Any(phrase => folded.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    // U+2010 hyphen, U+2011 non-breaking hyphen, U+2013 en dash, U+2014 em dash.
    private static string FoldHyphens(string text) =>
        text.Replace('\u2010', '-').Replace('\u2011', '-').Replace('\u2013', '-').Replace('\u2014', '-');

    [GeneratedRegex(@"\bSTOP\b")]
    private static partial Regex StopKeyword();

    [GeneratedRegex(@"\b(?:reply|text)\s+stop\b", RegexOptions.IgnoreCase)]
    private static partial Regex StopDirective();
}
