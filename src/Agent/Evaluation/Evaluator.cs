using Agent.Common;
using Agent.Composition;
using Agent.Domain;
using Agent.Orchestration;
using Agent.Safety;

namespace Agent.Evaluation;

// Proves the thresholds rather than asserting them (DESIGN.md section 6): every case's
// already-executed result (captured once by the caller during the main batch pass - see
// ScoredRun) is compared against its own labeled `expected` oracle, not re-run through the
// agent a second time - so the scorecard describes exactly what was persisted, not a
// possibly-different resample (this matters for non-deterministic composers like OpenAI).
//
// Two constraint checks DESIGN.md lists separately - "no_pii_leak" and
// "safety_violations == 0" - collapse into one field here (SafetyViolationsWithinBudget):
// SafetyValidator itself returns per-category violation messages, but LeasingMessageAgent
// collapses them to a bare count (AgentDiagnostics.SafetyViolationCount) before they reach
// this evaluator, so scoring PII and safety-violation-count separately isn't supported by
// the diagnostics contract this evaluator actually receives. Similarly, DESIGN.md's fourth
// personalization token ("horizon cue") is dropped: unlike first name, property, and
// interest, it names no concrete, checkable text pattern - scoring it would mean inventing
// a rule with no evidence behind it.
public sealed class Evaluator : IEvaluator
{
    public Scorecard Evaluate(IReadOnlyList<ScoredRun> runs)
    {
        var scores = new List<RecordScore>(runs.Count);

        foreach (ScoredRun run in runs)
        {
            // Per-record isolation, same principle as CliRunner's main batch loop: a bug in
            // scoring one record (this project's own history includes exactly such a bug -
            // see TalkingPoints.md Sprint 7) must not discard every other record's score.
            // Exception type is captured alongside the message - a bare ex.Message alone
            // ("Value cannot be null. (Parameter 'key')") does not say what went wrong.
            try
            {
                scores.Add(Score(run));
            }
            catch (Exception ex)
            {
                scores.Add(RecordScore.Unscoreable(run.ProspectCase.TaskId, ex.ToDiagnosticString()));
            }
        }

        return new Scorecard(scores);
    }

    private static RecordScore Score(ScoredRun run)
    {
        ProspectCase prospectCase = run.ProspectCase;
        ExpectedOutcome? expected = prospectCase.Expected;

        if (expected is null)
        {
            return RecordScore.Unscoreable(
                prospectCase.TaskId,
                $"Case '{prospectCase.TaskId}' has no expected outcome to score against.");
        }

        AgentRunResult result = run.Result;
        NextMessage? actual = result.Output.NextMessage;
        CaseConstraints constraints = prospectCase.Assertions.Constraints;
        CaseThresholds thresholds = prospectCase.Thresholds;
        string? requiredCtaType = PrimaryCtaVocabulary.ToCtaType(constraints.PrimaryCta);

        bool channelMatches = expected.NextMessage?.Channel == actual?.Channel;
        bool nextActionTypeMatches = expected.NextAction.Type == result.Output.NextAction.Type;

        bool optOutPresent = actual is null
            || !constraints.IncludeOptOutInstructions
            || ContainsOptOutPhrase(actual);

        // Trivially satisfied when the case states no primary CTA at all - there is
        // nothing to check the actual output against (confirmed real, not hypothetical:
        // two records in the actual interview hold-out have no primary_cta constraint).
        bool primaryCtaPresent = actual is null || requiredCtaType is null || actual.Cta?.Type == requiredCtaType;

        bool safetyWithinBudget = result.Diagnostics.SafetyViolationCount <= thresholds.SafetyViolationsMax;

        double personalizationScore = actual is null ? 1.0 : ComputePersonalizationScore(prospectCase.Input, actual.Body);
        bool personalizationScoreMet = personalizationScore >= thresholds.PersonalizationScoreMin;

        bool latencyWithinBudget = run.LatencyMs <= thresholds.P95LatencyMs;

        return new RecordScore(
            prospectCase.TaskId,
            channelMatches,
            nextActionTypeMatches,
            optOutPresent,
            primaryCtaPresent,
            safetyWithinBudget,
            personalizationScore,
            personalizationScoreMet,
            run.LatencyMs,
            latencyWithinBudget);
    }

    // Mirrors SafetyValidator.Validate's own search text (Subject+Body when Subject is
    // present), so this checks the same opt-out phrasing the validator actually enforces,
    // not just the Body half of it.
    private static bool ContainsOptOutPhrase(NextMessage message)
    {
        string text = message.Subject is { Length: > 0 }
            ? $"{message.Subject} {message.Body}"
            : message.Body;

        return SafetyValidator.OptOutPhrases.Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    private static double ComputePersonalizationScore(ProspectContext input, string body)
    {
        var tokens = new List<string> { input.Profile.FirstName, input.PropertyName };

        if (input.Profile.Amenities.Count > 0)
        {
            tokens.AddRange(input.Profile.Amenities);
        }

        if (input.Profile.City.Length > 0)
        {
            tokens.Add(input.Profile.City);
        }

        int matched = tokens.Count(token => body.Contains(token, StringComparison.OrdinalIgnoreCase));
        return (double)matched / tokens.Count;
    }
}
