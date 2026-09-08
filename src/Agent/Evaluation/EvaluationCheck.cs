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
}
