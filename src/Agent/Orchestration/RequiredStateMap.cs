using Agent.Common;

namespace Agent.Orchestration;

// D42's first part: the answer to assertions.required_states, which until now was parsed and
// read by nothing, so a record asserting a state was answered with silence. Every name in the
// record's own list gets a verdict, from the source D42 names for it, and any other name is
// recorded by name as one this program has no check for (A14).
//
// The keys are the record's own strings, written to the wire verbatim: AgentJsonOptions sets
// PropertyNamingPolicy and not DictionaryKeyPolicy, so a name the record spelled some other
// way comes back spelled the way the record spelled it, which is what makes the answer
// traceable to the assertion. Ordinal comparison for the same reason: these are data values,
// not identifiers this program coins, so a differently cased name is a different name and has
// no check.
//
// No rule is written for any name beyond the three below. In particular, the hold-out's
// renewal_offer_loaded stays unrecognized even though its records carry a renewal_offer_id,
// because the hold-out is an evaluation set and nothing is fitted to it (D9, A19).
public static class RequiredStateMap
{
    private const string ConsentVerifiedState = "consent_verified";
    private const string FairHousingCheckPassedState = "fair_housing_check_passed";
    private const string BrandStyleAppliedState = "brand_style_applied";

    // An absent or empty list is a question with no items, so the answer is an empty map
    // rather than null: null would be indistinguishable from a run that never built one.
    // O(s) in the number of names the record asserts.
    public static IReadOnlyDictionary<string, RequiredStateVerdict> For(
        IReadOnlyList<string?>? requiredStates,
        RequiredStateVerdict consentVerified,
        RequiredStateVerdict fairHousingCheckPassed,
        RequiredStateVerdict brandStyleApplied)
    {
        var verdicts = new Dictionary<string, RequiredStateVerdict>(StringComparer.Ordinal);

        // A name asserted twice collapses to one entry: the verdict is a function of the name
        // and the run, so the second entry would repeat the first.
        foreach (string? state in requiredStates ?? [])
        {
            // D47: JSON supplies a null element whatever the element type says, and
            // RespectNullableAnnotations does not reach inside a collection. A null or blank
            // name asserts no state, so it is skipped; it used to reach the indexer below and
            // take the whole record out with an ArgumentNullException (A16).
            if (Presence.IsAbsent(state))
            {
                continue;
            }

            verdicts[state!] = state switch
            {
                ConsentVerifiedState => consentVerified,
                FairHousingCheckPassedState => fairHousingCheckPassed,
                BrandStyleAppliedState => brandStyleApplied,
                _ => RequiredStateVerdict.NoCheckDefined,
            };
        }

        return verdicts;
    }
}
