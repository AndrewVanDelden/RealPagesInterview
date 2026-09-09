namespace Agent.Safety;

// The four checks of D38, each answering for itself. The four are named members rather
// than a list, so a result that is missing a check cannot be constructed and VerdictOf
// never has to invent an answer.
//
// Violations is the flat list the compose-validate loop feeds back as rejection reasons,
// the agent counts into safety_violation_count, and the review queue of D43 reads. It is
// computed once here, in a property initializer, and not rebuilt by a getter on every
// read. The same caveat Scorecard carries applies: a `with` copy that replaces one of the
// four checks keeps this list and the Checks list unchanged, so build a new
// SafetyValidationResult rather than copying one.
public sealed record SafetyValidationResult(
    SafetyCheckResult OptOutInstructions,
    SafetyCheckResult SocialSecurityNumber,
    SafetyCheckResult LongDigitRun,
    SafetyCheckResult FairHousing)
{
    public IReadOnlyList<SafetyCheckResult> Checks { get; } =
        [OptOutInstructions, SocialSecurityNumber, LongDigitRun, FairHousing];

    public IReadOnlyList<string> Violations { get; } =
        Flatten(OptOutInstructions, SocialSecurityNumber, LongDigitRun, FairHousing);

    // O(1): Checks always holds exactly the four members above.
    public SafetyCheckVerdict VerdictOf(SafetyCheck check) => Checks.First(result => result.Check == check).Verdict;

    // The same lines as Violations, each still attached to the check that produced it, for the
    // review queue of D43. Built on call rather than in an initializer: every record pays for
    // Violations, only a suppressed one pays for this.
    // O(d) in the total number of detail lines the four checks carry.
    public IReadOnlyList<SafetyViolation> ViolationsByCheck() =>
        Checks
            .Where(result => result.Verdict == SafetyCheckVerdict.Failed)
            .SelectMany(result => result.Details.Select(detail => new SafetyViolation(result.Check, detail)))
            .ToList();

    // O(d) in the total number of detail lines the four checks carry.
    private static IReadOnlyList<string> Flatten(params SafetyCheckResult[] checks) =>
        checks
            .Where(check => check.Verdict == SafetyCheckVerdict.Failed)
            .SelectMany(check => check.Details)
            .ToList();
}
