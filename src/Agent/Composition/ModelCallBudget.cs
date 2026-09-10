using Agent.Domain;

namespace Agent.Composition;

// D28 and playbook step 49: what bounds one model call. The batch's strictest stated
// p95_latency_ms is the number the run is judged against (A15), so it is also the number one
// call, its retry included, is allowed to take; the client divides it into per-attempt
// timeouts (OpenAiCompletionClient.PerAttemptTimeout). A batch that states none leaves the
// client on its own default.
// The tension is real and stated rather than hidden: a 2000 ms budget is shorter than many
// model calls, so a run against a set that states one will see timeouts, fall back to the
// offline composer, and say so in the diagnostics (D24).
public static class ModelCallBudget
{
    // O(n) in the batch size: one pass over the records, before any call is made; O(1) when an
    // override is given.
    // D70: an evaluation run may state its own budget, which replaces the records' rather than
    // taking the stricter of the two, or it could never raise one. The p95 check still reads the
    // records' own budget (Evaluator), so a run under an override reports the miss.
    public static TimeSpan? PerCallBudget(IReadOnlyList<ProspectCase> cases, TimeSpan? evaluationOverride = null)
    {
        if (evaluationOverride is not null)
        {
            return evaluationOverride;
        }

        int? strictestMs = cases
            .Select(prospectCase => prospectCase.ThresholdsOrEmpty.P95LatencyMs)
            .Where(budget => budget is > 0)
            .Min();

        return strictestMs is { } budgetMs ? TimeSpan.FromMilliseconds(budgetMs) : null;
    }
}
