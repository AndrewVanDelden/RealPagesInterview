using Agent.Common;
using Agent.Domain;
using Agent.Safety;
using Microsoft.Extensions.Logging;

namespace Agent.Evaluation;

// The scorecard of DESIGN.md section 6, one verdict per check per record, proven able to
// fail before it measures anything (D13, playbook step 33). Every case's already-executed
// output (ScoredRun) is compared against its own labeled `expected` oracle; the agent is
// never re-run here. Deterministic fields are exact; fuzzy fields use the strongest
// checkable proxy: fact coverage for personalization, stop-word detection for language.
// The semantic judge for next_action.type is Sprint 6 (D15).
public sealed class Evaluator(ILogger<Evaluator>? logger = null) : IEvaluator
{
    private readonly ILogger<Evaluator> log = logger.OrNullLogger();

    // O(n) in the batch size, each record scored once.
    public Scorecard Evaluate(IReadOnlyList<ScoredRun> runs)
    {
        var scores = new List<RecordScore>(runs.Count);

        foreach (ScoredRun run in runs)
        {
            // Per-record isolation (playbook step 34): a scoring bug on one record yields
            // one unscoreable row, never a lost batch. No TaskId scope here (D16): the row
            // carries the id, and the one log line names it.
            try
            {
                scores.Add(Score(run));
            }
            catch (Exception ex)
            {
                // D46: Score reads the composed message and the record's own labeled
                // content, so its exceptions go through the redacted formatter like every
                // other boundary that handles vendor, model, or prospect text. The raw
                // exception is never passed to the logger: ILogger's default formatter
                // appends Exception.ToString() in full regardless of the message template.
                string failure = ex.ToRedactedDiagnosticString();
                log.LogError("Scoring record '{TaskId}' failed: {ScoringFailure}.", run.ProspectCase.TaskId, failure);
                scores.Add(RecordScore.Unscoreable(run.ProspectCase.TaskId, failure));
            }
        }

        // The strictest stated budget bounds the batch p95; Min over int? is null when no
        // record states one.
        int? latencyBudgetMs = runs.Min(run => run.ProspectCase.ThresholdsOrEmpty.P95LatencyMs);

        return new Scorecard(scores, latencyBudgetMs);
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

        AgentOutput output = run.Output;
        NextMessage? expectedMessage = AsPresent(expected.NextMessage);
        NextMessage? actual = AsPresent(output.NextMessage);
        CaseConstraints constraints = prospectCase.ConstraintsOrEmpty;
        CaseThresholds thresholds = prospectCase.ThresholdsOrEmpty;
        ProspectContext context = prospectCase.ContextOrEmpty;
        string? text = actual is null ? null : MessageText(actual);

        CheckResult channel = Verdict(EffectiveChannel(expected.NextMessage) == EffectiveChannel(output.NextMessage));
        (CheckResult sendAtDay, CheckResult sendAtHour) = ScoreSendAt(expectedMessage?.SendAt, actual?.SendAt);
        CheckResult nextActionType = Verdict(expected.NextAction.Type == output.NextAction.Type);

        CheckResult optOut = text is null || !constraints.RequiresOptOutInstructions()
            ? CheckResult.NotMeasured
            : Verdict(OptOutInstructions.IsPresent(text));

        // D13 d: exact against the label's own call-to-action type, never against the
        // product's vocabulary table; the labels use types no constraint mapping states.
        CheckResult ctaType = actual is null || expectedMessage?.Cta?.Type is not { } expectedCtaType
            ? CheckResult.NotMeasured
            : Verdict(actual.Cta?.Type == expectedCtaType);

        CheckResult ctaPayload = actual is null ? CheckResult.NotMeasured : ScoreCtaPayload(actual);

        // Tokenized once and shared: BodyLanguage and Personalization both check words
        // drawn from the same message text.
        HashSet<string>? words = text is null ? null : MessageWords.Of(text);
        CheckResult bodyLanguage = ScoreLanguage(context.Language, words);

        // A15: a budget the record does not state is zero; a count the run did not record
        // is not measured (replay, D14).
        CheckResult safety = run.SafetyViolationCount is not { } violationCount
            ? CheckResult.NotMeasured
            : Verdict(violationCount <= (thresholds.SafetyViolationsMax ?? 0));

        double? personalizationScore = words is null ? null : ComputePersonalizationScore(context, words);
        CheckResult personalization = personalizationScore is not { } score || thresholds.PersonalizationScoreMin is not { } minimumScore
            ? CheckResult.NotMeasured
            : Verdict(score >= minimumScore);

        return new RecordScore(
            TaskId: prospectCase.TaskId,
            Channel: channel,
            SendAtDay: sendAtDay,
            SendAtHour: sendAtHour,
            NextActionType: nextActionType,
            OptOut: optOut,
            CtaType: ctaType,
            CtaPayload: ctaPayload,
            BodyLanguage: bodyLanguage,
            Safety: safety,
            PersonalizationScore: personalizationScore,
            Personalization: personalization,
            LatencyMs: run.LatencyMs);
    }

    private static CheckResult Verdict(bool passed) => passed ? CheckResult.Passed : CheckResult.Failed;

    // Three spellings of "no message" score as the same channel: a null next_message, the
    // oracle's object with channel "none" (D3), and DESIGN.md section 2's null channel.
    // Internal rather than private: SemanticJudge shares this exact rule (BodyOf) rather
    // than restating it, so the two agree on what "no message" means without a second copy.
    internal static CommunicationChannel EffectiveChannel(NextMessage? message) =>
        message?.Channel ?? CommunicationChannel.None;

    // A suppressed message has no body, send time, or call to action to check.
    internal static NextMessage? AsPresent(NextMessage? message) =>
        EffectiveChannel(message) == CommunicationChannel.None ? null : message;

    // Subject plus body, the same text the validator checks: the labels put the property
    // name and the opt-out phrase in either.
    private static string MessageText(NextMessage message) =>
        message.Subject is { Length: > 0 }
            ? $"{message.Subject} {message.Body}"
            : message.Body ?? string.Empty;

    // D4 and D6: to the day and to the hour, compared in the label's own offset so the same
    // instant written in UTC still matches.
    private static (CheckResult Day, CheckResult Hour) ScoreSendAt(DateTimeOffset? expectedAt, DateTimeOffset? actualAt)
    {
        if (expectedAt is not { } expectedSendAt)
        {
            return (CheckResult.NotMeasured, CheckResult.NotMeasured);
        }

        if (actualAt is not { } actualSendAt)
        {
            return (CheckResult.Failed, CheckResult.Failed);
        }

        DateTimeOffset aligned = actualSendAt.ToOffset(expectedSendAt.Offset);
        bool sameDay = aligned.Date == expectedSendAt.Date;

        return (Verdict(sameDay), Verdict(sameDay && aligned.Hour == expectedSendAt.Hour));
    }

    // A10: sms carries reply options, email carries a link; any other channel has no
    // payload rule in the evidence.
    private static CheckResult ScoreCtaPayload(NextMessage message) => message.Channel switch
    {
        CommunicationChannel.Sms => Verdict(message.Cta?.Options is { Count: > 0 }),
        CommunicationChannel.Email => Verdict(message.Cta?.Link is not null),
        _ => CheckResult.NotMeasured,
    };

    // A13 and D13 c: not measured without a message, without a stated language, or for a
    // language the detector does not know; a body with no stop words at all fails.
    private static CheckResult ScoreLanguage(string? languageTag, HashSet<string>? words)
    {
        if (words is null || !LanguageDetector.TryParseTag(languageTag, out MessageLanguage stated))
        {
            return CheckResult.NotMeasured;
        }

        return Verdict(LanguageDetector.Detect(words) == stated);
    }

    // D13 a: coverage of the first name and the property name. A record carrying neither
    // has nothing to personalize and scores 1.0.
    private static double ComputePersonalizationScore(ProspectContext context, HashSet<string> words)
    {
        var facts = new List<string>(2);

        if (Present(context.ProfileOrEmpty.FirstName) is { } firstName)
        {
            facts.Add(firstName);
        }

        if (Present(context.PropertyName) is { } propertyName)
        {
            facts.Add(propertyName);
        }

        if (facts.Count == 0)
        {
            return 1.0;
        }

        int covered = facts.Count(fact => MessageWords.Covers(words, fact));

        return (double)covered / facts.Count;
    }

    private static string? Present(string? value) => Presence.IsAbsent(value) ? null : value;
}
