using System.Text.RegularExpressions;
using Agent.Domain;

namespace Agent.Safety;

// Keyword/pattern heuristic, not a comprehensive fair-housing compliance system: see
// docs/CODE_REVIEW.md and TalkingPoints.md Sprint 4 for the explicit scope note.
public sealed partial class SafetyValidator : ISafetyValidator
{
    private static readonly string[] OptOutPhrases = ["stop", "opt out", "opt-out", "unsubscribe"];

    private static readonly string[] ProtectedClassAndSteeringTerms =
    [
        "race", "racial", "religion", "religious", "national origin", "familial status",
        "disability", "disabled", "handicap", "gender", "color", "ethnicity",
        "families only", "no children", "no kids", "singles only", "adults only",
        "christian", "muslim", "jewish", "catholic",
    ];

    public ValidationResult Validate(NextMessage message, CaseConstraints constraints)
    {
        var violations = new List<string>();

        if (constraints.IncludeOptOutInstructions && !ContainsAny(message.Body, OptOutPhrases))
        {
            violations.Add("Missing required opt-out instructions.");
        }

        if (constraints.NoPiiLeak && (SsnPattern().IsMatch(message.Body) || LongDigitRunPattern().IsMatch(message.Body)))
        {
            violations.Add("Body appears to contain a leaked personal identifier (SSN-like or long numeric sequence).");
        }

        string? steeringTerm = FindFirst(message.Body, ProtectedClassAndSteeringTerms);
        if (steeringTerm is not null)
        {
            violations.Add($"Body contains protected-class or steering language: '{steeringTerm}'.");
        }

        return new ValidationResult(violations, FairHousingCheckPassed: violations.Count == 0);
    }

    private static bool ContainsAny(string text, IReadOnlyList<string> phrases) =>
        phrases.Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    private static string? FindFirst(string text, IReadOnlyList<string> terms) =>
        terms.FirstOrDefault(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b")]
    private static partial Regex SsnPattern();

    [GeneratedRegex(@"\b\d{13,19}\b")]
    private static partial Regex LongDigitRunPattern();
}
