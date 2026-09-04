using System.Text.RegularExpressions;
using Agent.Domain;

namespace Agent.Safety;

// Keyword/pattern heuristic, not a comprehensive fair-housing compliance system: see
// docs/CODE_REVIEW.md and TalkingPoints.md Sprint 4 for the explicit scope note.
//
// NoSensitiveDiscrimination (CaseConstraints) is intentionally never read here: the
// protected-class/steering check always runs regardless of its value. Fair housing law
// has no legitimate per-case opt-out, unlike opt-out messaging (transactional exemptions
// are real) or generic PII sensitivity (which can vary case by case).
public sealed partial class SafetyValidator : ISafetyValidator
{
    // Specific, standard opt-out phrasing rather than the bare word "stop": a bare "stop"
    // would substring-match unrelated text like "bus stop" even with word-boundary
    // anchoring, since "stop" is already a complete word there. Matches
    // TemplateMessageComposer's actual generated text ("Reply STOP to opt out.").
    // Internal, not private: Agent.Evaluation.Evaluator reuses this exact list so the
    // eval harness checks the same opt-out phrasing the validator actually enforces,
    // rather than an independently-maintained (and possibly drifting) duplicate.
    internal static readonly string[] OptOutPhrases = ["reply stop", "text stop", "opt out", "opt-out", "unsubscribe"];

    private static readonly string[] ProtectedClassAndSteeringTerms =
    [
        "race", "racial", "religion", "religious", "national origin", "familial status",
        "disability", "disabled", "handicap", "gender", "color", "ethnicity",
        "families only", "no children", "no kids", "singles only", "adults only",
        "christian", "muslim", "jewish", "catholic",
    ];

    public SafetyValidationResult Validate(NextMessage message, CaseConstraints constraints)
    {
        var violations = new List<string>();
        string text = message.Subject is { Length: > 0 }
            ? $"{message.Subject} {message.Body}"
            : message.Body;

        if (constraints.IncludeOptOutInstructions && FindFirst(text, OptOutPhrases) is null)
        {
            violations.Add("Missing required opt-out instructions.");
        }

        if (constraints.NoPiiLeak && (SsnPattern().IsMatch(text) || LongDigitRunPattern().IsMatch(text)))
        {
            violations.Add("Body appears to contain a leaked personal identifier (SSN-like or long numeric sequence).");
        }

        string? steeringTerm = FindFirst(text, ProtectedClassAndSteeringTerms, wholeWord: true);
        if (steeringTerm is not null)
        {
            violations.Add($"Body contains protected-class or steering language: '{steeringTerm}'.");
        }

        return new SafetyValidationResult(violations, FairHousingCheckPassed: violations.Count == 0);
    }

    private static string? FindFirst(string text, IReadOnlyList<string> terms, bool wholeWord = false) =>
        terms.FirstOrDefault(term => wholeWord
            ? Regex.IsMatch(text, $@"\b{Regex.Escape(term)}\b", RegexOptions.IgnoreCase)
            : text.Contains(term, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b")]
    private static partial Regex SsnPattern();

    // Same 13-19 total digit threshold as a bare unbroken run, but tolerant of the
    // space/dash grouping real formatted numbers (e.g. credit cards) actually use.
    [GeneratedRegex(@"\b\d(?:[- ]?\d){12,18}\b")]
    private static partial Regex LongDigitRunPattern();
}
