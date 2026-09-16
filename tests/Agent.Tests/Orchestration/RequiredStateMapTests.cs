using Agent.Orchestration;
using Xunit;

namespace Agent.Tests.Orchestration;

// A14: every name in the record's own required_states gets a verdict,
// and a name this program has no check for is recorded as not earned, by name.
public class RequiredStateMapTests
{
    private static IReadOnlyDictionary<string, RequiredStateVerdict> For(
        IReadOnlyList<string>? requiredStates,
        RequiredStateVerdict consentVerified = RequiredStateVerdict.Earned,
        RequiredStateVerdict fairHousingCheckPassed = RequiredStateVerdict.Earned,
        RequiredStateVerdict brandStyleApplied = RequiredStateVerdict.Earned,
        RequiredStateVerdict renewalOfferLoaded = RequiredStateVerdict.Earned) =>
        RequiredStateMap.For(requiredStates, consentVerified, fairHousingCheckPassed, brandStyleApplied, renewalOfferLoaded);

    // The three names both samples assert, each answered by its own source: the channel
    // selector's read of consent, the FairHousing check alone rather than every check, and the
    // brand-style validator.
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

    // A name this program has no check for is not earned, and the verdict says why: no check
    // exists for the name.
    [Fact]
    public void For_AStateWithNoCheck_RecordsItByNameAsNoCheckDefined()
    {
        IReadOnlyDictionary<string, RequiredStateVerdict> map = For(["consent_verified", "lease_signed"]);

        Assert.Equal(RequiredStateVerdict.NoCheckDefined, map["lease_signed"]);
        Assert.Equal(RequiredStateVerdict.Earned, map["consent_verified"]);
    }

    // renewal_offer_loaded is answered by the lookup that loads the record's offer from the
    // property data.
    [Fact]
    public void For_RenewalOfferLoaded_AnswersFromTheOfferLookup()
    {
        IReadOnlyDictionary<string, RequiredStateVerdict> map = For(["renewal_offer_loaded"], renewalOfferLoaded: RequiredStateVerdict.NotEarned);

        Assert.Equal(RequiredStateVerdict.NotEarned, map["renewal_offer_loaded"]);
    }

    // A record that asserts nothing has asked no question, so the map is the complete answer
    // to a list with no items rather than an absent answer. Absent assertions (every
    // member below task_id and channel_preferences is optional) and an absent list
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

    // JSON supplies a null list element whatever the element type says, and
    // RespectNullableAnnotations does not reach inside a collection, so this list really can
    // hold one. A null name asserts no state, so it is skipped rather than thrown on: it used
    // to reach the dictionary indexer and take the whole record out with an
    // ArgumentNullException, which A16 says an input shape must never do.
    [Fact]
    public void For_ANullName_IsSkippedAndTheRestAreStillAnswered()
    {
        IReadOnlyDictionary<string, RequiredStateVerdict> map = For(
            ["consent_verified", null!],
            RequiredStateVerdict.Earned);

        Assert.Equal(["consent_verified"], map.Keys);
    }

    // A name that is blank or only whitespace names no state either, and Presence.IsAbsent is
    // the one definition of absent this program already uses for a stated-but-empty value.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void For_ABlankName_IsSkipped(string blank)
    {
        Assert.Empty(For([blank]));
    }
}
