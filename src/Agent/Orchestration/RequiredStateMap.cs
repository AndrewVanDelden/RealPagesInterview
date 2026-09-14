using Agent.Common;

namespace Agent.Orchestration;

// The answer to assertions.required_states, so a record asserting a state is never answered
// with silence: each name in the record's own list gets a verdict from the step that proves it,
// and any other name is recorded as one this program has no check for (A14). Keys are the
// record's own strings, written verbatim (AgentJsonOptions sets no DictionaryKeyPolicy) and
// compared ordinally: data values, not names this program coins, so the answer traces to the
// assertion and a differently cased name is a different name with no check. renewal_offer_loaded
// is earned by finding the record's renewal offer in the property data, which stands in for the
// property management system that holds the offer.
public static class RequiredStateMap
{
    private const string ConsentVerifiedState = "consent_verified";
    private const string FairHousingCheckPassedState = "fair_housing_check_passed";
    private const string BrandStyleAppliedState = "brand_style_applied";
    private const string RenewalOfferLoadedState = "renewal_offer_loaded";

    // An absent or empty list is a question with no items, so the answer is an empty map
    // rather than null: null would be indistinguishable from a run that never built one.
    // O(s) in the number of names the record asserts.
    public static IReadOnlyDictionary<string, RequiredStateVerdict> For(
        IReadOnlyList<string?>? requiredStates,
        RequiredStateVerdict consentVerified,
        RequiredStateVerdict fairHousingCheckPassed,
        RequiredStateVerdict brandStyleApplied,
        RequiredStateVerdict renewalOfferLoaded)
    {
        var verdicts = new Dictionary<string, RequiredStateVerdict>(StringComparer.Ordinal);

        // A name asserted twice collapses to one entry: the verdict is a function of the name
        // and the run, so the second entry would repeat the first.
        foreach (string? state in requiredStates ?? [])
        {
            // JSON supplies a null element whatever the element type says, and
            // RespectNullableAnnotations does not reach inside a collection. A null or blank
            // name asserts no state, so it is skipped rather than reaching the indexer below,
            // whose ArgumentNullException would take the whole record out (A16).
            if (Presence.IsAbsent(state))
            {
                continue;
            }

            verdicts[state!] = state switch
            {
                ConsentVerifiedState => consentVerified,
                FairHousingCheckPassedState => fairHousingCheckPassed,
                BrandStyleAppliedState => brandStyleApplied,
                RenewalOfferLoadedState => renewalOfferLoaded,
                _ => RequiredStateVerdict.NoCheckDefined,
            };
        }

        return verdicts;
    }
}
