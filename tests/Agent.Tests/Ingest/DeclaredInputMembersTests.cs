using Agent.Domain;
using Agent.Ingest;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Ingest;

// The hold-out is the evidence the message rules are fitted to, so every fact its inputs carry
// binds to a typed member the prompt reads. A fact that arrived only as an unknown member would
// be listed in the diagnostics and never shown to the model.
public class DeclaredInputMembersTests
{
    [Fact]
    public void Describe_EveryHoldoutRecord_ListsNoUnknownInputMember()
    {
        foreach (ProspectCase prospectCase in RealAgentFactory.ReadCases("holdout_12.jsonl"))
        {
            Assert.DoesNotContain(IngestNotes.Describe(prospectCase).UnknownMembers, path => path.StartsWith("input.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ReadCases_Holdout_BindsEachStageFactToItsTypedMember()
    {
        IReadOnlyList<ProspectCase> cases = RealAgentFactory.ReadCases("holdout_12.jsonl");
        ProspectContext ContextOf(string taskId) => cases.Single(prospectCase => prospectCase.TaskId == taskId).ContextOrEmpty;

        ProspectContext renewal = ContextOf("resident_renewal_90day_notice");
        Assert.Equal("A‑204", renewal.Unit);
        Assert.Equal(new DateOnly(2026, 3, 10), renewal.LeaseEndDate);
        Assert.Equal(10, renewal.ProfileOrEmpty.TenureMonths);

        ProspectContext welcome = ContextOf("resident_welcome_day0");
        Assert.Equal(new DateOnly(2025, 12, 12), welcome.MoveInDate);
        Assert.Equal(["packages", "amenities"], welcome.ProfileOrEmpty.FeaturesEnablement);

        Assert.Equal("eligible", ContextOf("resident_loyalty_engage").ProfileOrEmpty.LoyaltyStatus);
        Assert.Equal("REN‑A204‑2026", ContextOf("resident_renewal_undecided_followup").RenewalOfferId);
        Assert.Equal(DateTimeOffset.Parse("2025-12-08T14:00:00-06:00"), ContextOf("prospect_no_show_reengage").MissedTourTime);

        ProspectContext cancellation = ContextOf("prospect_cancellation_manager_cross_sell");
        Assert.Equal("schedule_conflict", cancellation.CancellationReason);
        Assert.Equal(1700m, cancellation.ProfileOrEmpty.BudgetMax);
    }
}
