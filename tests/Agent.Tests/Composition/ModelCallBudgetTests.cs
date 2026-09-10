using Agent.Composition;
using Agent.Domain;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Composition;

// D28 and playbook step 49: the per-call timeout is the strictest latency budget the batch
// states, so the bound on a model call is a number the input asked for rather than a default
// nobody chose. The same "strictest stated budget" rule the evaluator scores the p95 against
// (A15), so the call is bounded by the number the run is judged by.
public class ModelCallBudgetTests
{
    private static ProspectCase WithBudget(int? p95LatencyMs) =>
        SampleProspectCases.Minimal() with { Thresholds = new CaseThresholds(p95LatencyMs, null, null, null) };

    [Fact]
    public void PerCallBudget_RecordsStateDifferentBudgets_TakesTheStrictest()
    {
        TimeSpan? timeout = ModelCallBudget.PerCallBudget([WithBudget(2000), WithBudget(800), WithBudget(5000)]);

        Assert.Equal(TimeSpan.FromMilliseconds(800), timeout);
    }

    // A15: a record with no stated budget is not a budget of zero, and a batch where nobody
    // states one leaves the client on its own default.
    [Fact]
    public void PerCallBudget_SomeRecordsStateNoBudget_IgnoresThoseRecords()
    {
        TimeSpan? timeout = ModelCallBudget.PerCallBudget([WithBudget(null), WithBudget(1500)]);

        Assert.Equal(TimeSpan.FromMilliseconds(1500), timeout);
    }

    [Fact]
    public void PerCallBudget_NoRecordStatesABudget_ReturnsNull()
    {
        Assert.Null(ModelCallBudget.PerCallBudget([WithBudget(null)]));
    }

    [Fact]
    public void PerCallBudget_EmptyBatch_ReturnsNull()
    {
        Assert.Null(ModelCallBudget.PerCallBudget([]));
    }

    // A budget of zero or less bounds nothing a call could satisfy, so it is not a timeout
    // this program can honor: the client keeps its own default and the p95 check reports the
    // miss.
    [Fact]
    public void PerCallBudget_BudgetIsNotPositive_ReturnsNull()
    {
        Assert.Null(ModelCallBudget.PerCallBudget([WithBudget(0)]));
    }

    // D70: an evaluation run may state its own budget for the composer's model calls, because
    // on these sets the records' 2000 ms cannot fit one completion, measured at 2 to 4 s a call.
    // The override replaces the records' budget rather than taking the stricter of the two, or
    // it could never raise a budget at all.
    [Fact]
    public void PerCallBudget_OverrideGiven_ReplacesEveryRecordsBudget()
    {
        TimeSpan? timeout = ModelCallBudget.PerCallBudget([WithBudget(2000), WithBudget(800)], TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), timeout);
    }
}
