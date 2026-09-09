using Agent.Orchestration;
using Xunit;

namespace Agent.Tests.Orchestration;

// D42's first part and A14: every name in the record's own required_states gets a verdict,
// and a name this program has no check for is recorded as not earned, by name.
public class RequiredStateMapTests
{
    private static IReadOnlyDictionary<string, RequiredStateVerdict> For(
        IReadOnlyList<string>? requiredStates,
        RequiredStateVerdict consentVerified = RequiredStateVerdict.Earned,
        RequiredStateVerdict fairHousingCheckPassed = RequiredStateVerdict.Earned,
        RequiredStateVerdict brandStyleApplied = RequiredStateVerdict.Earned) =>
        RequiredStateMap.For(requiredStates, consentVerified, fairHousingCheckPassed, brandStyleApplied);

    // The three names both samples assert, each answered by its own source: the consent gate,
    // the FairHousing check alone (D38), and the brand-style validator.
    [Fact]
    public void For_TheThreeStatesWithChecks_AnswersEachFromItsOwnSource()
    {
        IReadOnlyDictionary<string, RequiredStateVerdict> map = For(
            ["consent_verified", "fair_housing_check_passed", "brand_style_applied"],
            RequiredStateVerdict.Earned,
            RequiredStateVerdict.NotEarned,
            RequiredStateVerdict.NotEvaluated);

        Assert.Equal(RequiredStateVerdict.Earned, map["consent_verified"]);
        Assert.Equal(RequiredStateVerdict.NotEarned, map["fair_housing_check_passed"]);
        Assert.Equal(RequiredStateVerdict.NotEvaluated, map["brand_style_applied"]);
    }

    // D42 and docs/CODE_REVIEW.md: the hold-out names renewal_offer_loaded and some of those
    // records carry a renewal_offer_id a rule could obviously be fitted to, which is exactly
    // why no rule is written for it (D9, A19). Not earned is the honest answer, and the
    // verdict says why it is not earned: this program has no check for the name.
    [Fact]
    public void For_AStateWithNoCheck_RecordsItByNameAsNoCheckDefined()
    {
        IReadOnlyDictionary<string, RequiredStateVerdict> map = For(["consent_verified", "renewal_offer_loaded"]);

        Assert.Equal(RequiredStateVerdict.NoCheckDefined, map["renewal_offer_loaded"]);
        Assert.Equal(RequiredStateVerdict.Earned, map["consent_verified"]);
    }

    // A record that asserts nothing has asked no question, so the map is the complete answer
    // to a list with no items rather than an absent answer. Absent assertions (D1: every
    // member below task_id, consent and channel_preferences is optional) and an absent list
    // reach the same place.
    [Fact]
    public void For_NoRequiredStates_IsAnEmptyMapRatherThanNull()
    {
        Assert.Empty(For(null));
        Assert.Empty(For([]));
    }

    // The list is free text, so it can name one state twice. The verdict is a function of the
    // name and the run, so the two entries would be identical: the map collapses them and
    // nothing is lost.
    [Fact]
    public void For_ADuplicatedName_CollapsesToOneEntry()
    {
        IReadOnlyDictionary<string, RequiredStateVerdict> map = For(
            ["consent_verified", "consent_verified"],
            RequiredStateVerdict.NotEarned);

        Assert.Equal(["consent_verified"], map.Keys);
        Assert.Equal(RequiredStateVerdict.NotEarned, map["consent_verified"]);
    }
}
