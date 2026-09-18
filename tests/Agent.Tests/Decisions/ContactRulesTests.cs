using Agent.Common;
using Agent.Decisions;
using Agent.Domain;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Decisions;

// Records that must not be sent a message whatever their consent says: some get nothing, some go to
// a person. Each rule is shown firing on its own and not firing on the committed records a message is
// still right for.
public class ContactRulesTests
{
    private static readonly DateTimeOffset ReferenceTime = DateTimeOffset.Parse("2026-03-09T09:00:00-06:00");
    private static readonly DateOnly ReferenceDate = new(2026, 3, 9);

    private static ProspectCase Case(
        string? persona = "prospect",
        string? lifecycleStage = "new",
        ProspectProfile? profile = null,
        DateOnly? leaseEndDate = null,
        DateTimeOffset? missedTourTime = null,
        string? cancellationReason = null)
    {
        ProspectCase minimal = SampleProspectCases.Minimal(persona: persona, lifecycleStage: lifecycleStage);
        ProspectContext context = minimal.ContextOrEmpty with
        {
            Profile = profile ?? minimal.ContextOrEmpty.ProfileOrEmpty,
            LeaseEndDate = leaseEndDate,
            MissedTourTime = missedTourTime,
            CancellationReason = cancellationReason,
        };

        return minimal with { Input = context };
    }

    private static NextAction Fired(ProspectCase prospectCase)
    {
        Option<NextAction> action = ContactRules.Check(prospectCase, ReferenceTime, ReferenceDate);
        Assert.True(action.HasValue, "a contact rule should have fired");
        return action.Value;
    }

    private static void NoneFired(ProspectCase prospectCase) =>
        Assert.False(ContactRules.Check(prospectCase, ReferenceTime, ReferenceDate).HasValue);

    [Fact]
    public void Check_OptOutRecordedOnTheProfile_SendsNothingWhateverTheFlagsSay()
    {
        var profile = new ProspectProfile("Chloe", OptOutRequestedAt: DateTimeOffset.Parse("2026-03-05T18:12:00Z"));

        Assert.Equal(new NextAction(ActionTypes.NoOp, Reason: "opt_out_on_record"), Fired(Case("resident", "loyalty_engage", profile)));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(0)]
    public void Check_ProspectUnder18_SendsNothing(int age)
    {
        var profile = new ProspectProfile("Zoe", Age: age);

        Assert.Equal(new NextAction(ActionTypes.NoOp, Reason: "prospect_under_18"), Fired(Case(profile: profile)));
    }

    [Theory]
    [InlineData(18)]
    [InlineData(40)]
    public void Check_ProspectAged18OrOver_FiresNothing(int age) => NoneFired(Case(profile: new ProspectProfile("Zoe", Age: age)));

    // A persona the agent does not serve gets nothing. A guarantor is a leasing party the regression set
    // expects a message for, so it is served.
    [Theory]
    [InlineData("vendor")]
    [InlineData(" Contractor ")]
    public void Check_UnsupportedPersona_SendsNothing(string persona) =>
        Assert.Equal(new NextAction(ActionTypes.NoOp, Reason: "unsupported_persona"), Fired(Case(persona)));

    [Theory]
    [InlineData("prospect")]
    [InlineData("Resident")]
    [InlineData("guarantor")]
    [InlineData(null)]
    public void Check_ServedOrAbsentPersona_FiresNothing(string? persona) => NoneFired(Case(persona));

    [Fact]
    public void Check_DelinquentCollections_GoesToAPerson() =>
        Assert.Equal(
            new NextAction(ActionTypes.EscalateToHuman, Reason: "regulated_communication"),
            Fired(Case("resident", "delinquent_collections")));

    // A cancellation over screening is a person's call; a cancellation for a schedule conflict, the
    // hold-out's manager cancellation, still gets its message.
    [Fact]
    public void Check_CancelledOverScreening_GoesToAPerson() =>
        Assert.Equal(
            new NextAction(ActionTypes.EscalateToHuman, Reason: "manager_cancellation_requires_review"),
            Fired(Case("prospect", "cancelled_manager", cancellationReason: "screening_concern_criminal_history")));

    [Fact]
    public void Check_CancelledForAScheduleConflict_FiresNothing() =>
        NoneFired(Case("prospect", "cancelled_manager", cancellationReason: "schedule_conflict"));

    [Fact]
    public void Check_RenewalWhoseLeaseHasEnded_GoesToAPerson() =>
        Assert.Equal(
            new NextAction(ActionTypes.EscalateToHuman, Reason: "lease_end_date_in_past"),
            Fired(Case("resident", "renewal_window", leaseEndDate: new DateOnly(2026, 2, 1))));

    [Theory]
    [InlineData("2026-03-09")]
    [InlineData("2026-05-31")]
    public void Check_RenewalWhoseLeaseEndsTodayOrLater_FiresNothing(string leaseEnd) =>
        NoneFired(Case("resident", "renewal_window", leaseEndDate: DateOnly.Parse(leaseEnd)));

    [Fact]
    public void Check_NoShowWhoseTourIsStillAhead_SendsNothing() =>
        Assert.Equal(
            new NextAction(ActionTypes.NoOp, Reason: "missed_tour_time_in_future"),
            Fired(Case("prospect", "no_show", missedTourTime: DateTimeOffset.Parse("2026-03-14T14:00:00-06:00"))));

    [Fact]
    public void Check_NoShowWhoseTourHasPassed_FiresNothing() =>
        NoneFired(Case("prospect", "no_show", missedTourTime: DateTimeOffset.Parse("2026-03-08T14:00:00-06:00")));
}
