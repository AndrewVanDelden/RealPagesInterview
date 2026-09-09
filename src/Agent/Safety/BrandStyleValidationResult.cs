namespace Agent.Safety;

// Every brand rule the message failed, in the order BrandStyleRule declares them, so a
// diagnostic reports which rule failed rather than only that the message was off-voice.
// Empty means every rule held.
//
// Applied is a getter rather than a computed field, unlike Scorecard's tallies and
// SafetyValidationResult's Violations: it is one Count comparison, so there is no scan to
// hoist and no `with` copy that can carry a stale answer.
public sealed record BrandStyleValidationResult(IReadOnlyList<BrandStyleRule> FailedRules)
{
    public bool Applied => FailedRules.Count == 0;
}
