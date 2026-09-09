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
//
// D41: URL spans are removed before the keyword scan and before that scan only. STOP in a
// URL path is not an instruction the recipient can act on, so a message carrying no opt-out
// certified as having one, and because this is the definition the scorer uses too, the
// false pass propagated into the scorecard. The phrase and directive checks read the
// unmodified folded text, which is the behavior they already had.
public static partial class OptOutInstructions
{
    private static readonly string[] Phrases = ["opt out", "opt-out", "unsubscribe"];

    // O(n) in the text length.
    public static bool IsPresent(string text)
    {
        string folded = SafetyTextNormalizer.FoldHyphens(text);

        return StopKeyword().IsMatch(UrlSpan().Replace(folded, " "))
            || StopDirective().IsMatch(folded)
            || Phrases.Any(phrase => folded.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"\bSTOP\b")]
    private static partial Regex StopKeyword();

    [GeneratedRegex(@"\b(?:reply|text)\s+stop\b", RegexOptions.IgnoreCase)]
    private static partial Regex StopDirective();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex UrlSpan();
}
