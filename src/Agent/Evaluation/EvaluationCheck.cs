namespace Agent.Evaluation;

// The per-record checks of DESIGN.md section 6, in scorecard column order. Latency is not
// here: it is judged once per batch as a p95 (playbook step 32), not per record.
public enum EvaluationCheck
{
    Channel,
    SendAtDay,
    SendAtHour,
    NextActionType,
    OptOut,
    CtaType,
    CtaPayload,
    BodyLanguage,
    Safety,
    Personalization,

    // The judge's two questions. They are reported like every other check and counted
    // in the per-check line, and they are deliberately not part of a record's pass or fail.
    ActionSemantic,
    BodySemantic,
}

// Playbook step 31: the judge is one signal beside the deterministic checks and
// never overturns one, so the checks that decide a record's verdict are named here rather
// than being "all of them" at every call site.
public static class EvaluationChecks
{
    public static readonly EvaluationCheck[] All = Enum.GetValues<EvaluationCheck>();

    public static readonly EvaluationCheck[] Deterministic =
        [.. All.Where(check => check is not (EvaluationCheck.ActionSemantic or EvaluationCheck.BodySemantic))];
}
