using System.Text.RegularExpressions;

namespace Agent.Safety;

// The one definition of "carries an opt-out instruction", shared by the validator (which
// enforces it) and the evaluator (which measures it), so the agent never emits what the scorer
// rejects. Capital STOP as a whole word carries it in every language the sets contain ("Reply
// STOP", "Responde STOP"); lowercase "stop" is prose ("bus stop", "Please stop by") except
// directly after "reply" or "text". Unicode hyphens fold first, so "Opt-out" with U+2011 counts.
// URL spans are removed before the STOP scan only: STOP in a URL path is nothing a recipient can
// act on, and a false pass here would pass the scorecard too. The phrase and directive checks
// read the unmodified folded text.
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
