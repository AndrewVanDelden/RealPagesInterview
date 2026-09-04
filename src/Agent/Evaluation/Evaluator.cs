using System.Diagnostics;
using Agent.Composition;
using Agent.Domain;
using Agent.Orchestration;
using Agent.Safety;

namespace Agent.Evaluation;

// Proves the thresholds rather than asserting them (DESIGN.md section 6): every case is
// actually run through the agent, timed, and compared against its own labeled `expected`
// oracle - not a static assertion that the pipeline "should" behave a given way.
//
// Two constraint checks DESIGN.md lists separately - "no_pii_leak" and
// "safety_violations == 0" - collapse into one field here (SafetyViolationsWithinBudget):
// SafetyValidator produces a single unified violation count across PII, opt-out, and
// steering checks, not independently distinguishable categories, so pretending to score
// them as two separate signals would be reporting a distinction our system doesn't
// actually have. Similarly, DESIGN.md's fourth personalization token ("horizon cue") is
// dropped: unlike first name, property, and interest, it names no concrete, checkable
// text pattern - scoring it would mean inventing a rule with no evidence behind it.
public sealed class Evaluator(IMessageAgent agent) : IEvaluator
{
    public async Task<Scorecard> EvaluateAsync(IReadOnlyList<ProspectCase> cases, CancellationToken cancellationToken = default)
    {
        var scores = new List<RecordScore>(cases.Count);

        foreach (ProspectCase prospectCase in cases)
        {
            scores.Add(await ScoreAsync(prospectCase, cancellationToken));
        }

        return new Scorecard(scores);
    }

    private async Task<RecordScore> ScoreAsync(ProspectCase prospectCase, CancellationToken cancellationToken)
    {
        ExpectedOutcome expected = prospectCase.Expected
            ?? throw new ArgumentException(
                $"Case '{prospectCase.TaskId}' has no expected outcome to score against.", nameof(prospectCase));

        Stopwatch stopwatch = Stopwatch.StartNew();
        AgentRunResult result = await agent.RunAsync(prospectCase, cancellationToken);
        stopwatch.Stop();

        NextMessage? actual = result.Output.NextMessage;
        CaseConstraints constraints = prospectCase.Assertions.Constraints;
        CaseThresholds thresholds = prospectCase.Thresholds;
        string requiredCtaType = PrimaryCtaVocabulary.ToCtaType(constraints.PrimaryCta);

        bool channelMatches = expected.NextMessage?.Channel == actual?.Channel;
        bool nextActionTypeMatches = expected.NextAction.Type == result.Output.NextAction.Type;

        bool optOutPresent = actual is null
            || !constraints.IncludeOptOutInstructions
            || ContainsOptOutPhrase(actual.Body);

        bool primaryCtaPresent = actual is null || actual.Cta?.Type == requiredCtaType;

        bool safetyWithinBudget = result.Diagnostics.SafetyViolationCount <= thresholds.SafetyViolationsMax;

        double personalizationScore = actual is null ? 1.0 : ComputePersonalizationScore(prospectCase.Input, actual.Body);
        bool personalizationScoreMet = personalizationScore >= thresholds.PersonalizationScoreMin;

        double latencyMs = stopwatch.Elapsed.TotalMilliseconds;
        bool latencyWithinBudget = latencyMs <= thresholds.P95LatencyMs;

        return new RecordScore(
            prospectCase.TaskId,
            channelMatches,
            nextActionTypeMatches,
            optOutPresent,
            primaryCtaPresent,
            safetyWithinBudget,
            personalizationScore,
            personalizationScoreMet,
            latencyMs,
            latencyWithinBudget);
    }

    private static bool ContainsOptOutPhrase(string body) =>
        SafetyValidator.OptOutPhrases.Any(phrase => body.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    private static double ComputePersonalizationScore(ProspectContext input, string body)
    {
        var tokens = new List<string> { input.Profile.FirstName, input.PropertyName };

        if (input.Profile.AmenityInterest is { Count: > 0 } amenities)
        {
            tokens.AddRange(amenities);
        }
        else if (input.Profile.CityInterest is { Length: > 0 } city)
        {
            tokens.Add(city);
        }

        int matched = tokens.Count(token => body.Contains(token, StringComparison.OrdinalIgnoreCase));
        return (double)matched / tokens.Count;
    }
}
