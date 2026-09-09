namespace Agent.Evaluation;

// One record's verdict per check. PersonalizationScore is the coverage number behind the
// Personalization verdict; LatencyMs is carried for the batch p95 and is null when the run
// did not measure it (replay, D14).
public sealed record RecordScore(
    string TaskId,
    CheckResult Channel,
    CheckResult SendAtDay,
    CheckResult SendAtHour,
    CheckResult NextActionType,
    CheckResult OptOut,
    CheckResult CtaType,
    CheckResult CtaPayload,
    CheckResult BodyLanguage,
    CheckResult Safety,
    double? PersonalizationScore,
    CheckResult Personalization,
    double? LatencyMs,
    string? ScoringError = null,
    CheckResult ActionSemantic = CheckResult.NotMeasured,
    CheckResult BodySemantic = CheckResult.NotMeasured)
{
    // A record passes when it was scored and no deterministic check failed; a check that
    // was not measured neither passes nor fails it. The judge's two checks are deliberately
    // not here (D30, playbook step 31): a model's opinion is one signal beside the
    // deterministic checks and never turns a passing record into a failing one.
    public bool Passed =>
        ScoringError is null && EvaluationChecks.Deterministic.All(check => ResultOf(check) != CheckResult.Failed);

    public CheckResult ResultOf(EvaluationCheck check) => check switch
    {
        EvaluationCheck.Channel => Channel,
        EvaluationCheck.SendAtDay => SendAtDay,
        EvaluationCheck.SendAtHour => SendAtHour,
        EvaluationCheck.NextActionType => NextActionType,
        EvaluationCheck.OptOut => OptOut,
        EvaluationCheck.CtaType => CtaType,
        EvaluationCheck.CtaPayload => CtaPayload,
        EvaluationCheck.BodyLanguage => BodyLanguage,
        EvaluationCheck.Safety => Safety,
        EvaluationCheck.Personalization => Personalization,
        EvaluationCheck.ActionSemantic => ActionSemantic,
        EvaluationCheck.BodySemantic => BodySemantic,
        _ => throw new ArgumentOutOfRangeException(nameof(check), check, "Unknown evaluation check."),
    };

    // A case that could not be scored at all (no labeled expected outcome, or a scoring
    // bug), distinct from a case that was scored and failed. Keeps every attempted case
    // visible in the scorecard instead of aborting the batch (playbook step 34).
    public static RecordScore Unscoreable(string taskId, string reason) =>
        new(
            TaskId: taskId,
            Channel: CheckResult.NotMeasured,
            SendAtDay: CheckResult.NotMeasured,
            SendAtHour: CheckResult.NotMeasured,
            NextActionType: CheckResult.NotMeasured,
            OptOut: CheckResult.NotMeasured,
            CtaType: CheckResult.NotMeasured,
            CtaPayload: CheckResult.NotMeasured,
            BodyLanguage: CheckResult.NotMeasured,
            Safety: CheckResult.NotMeasured,
            PersonalizationScore: null,
            Personalization: CheckResult.NotMeasured,
            LatencyMs: null,
            ScoringError: reason);
}
