using Agent.Composition;
using Xunit;

namespace Agent.Tests.Composition;

// D62: the record the compose-validate loop sums and the two per-batch surfaces render. Null
// is "no model path ran", the same rule CompositionNotes.NetworkRetries already states, so
// adding null to anything must not invent a measurement.
public class ModelCostNotesTests
{
    [Fact]
    public void Add_BothNull_StaysNull()
    {
        Assert.Null(ModelCostNotes.Add(null, null));
    }

    [Fact]
    public void Add_OnlyTheLeftSideMeasuredAnything_KeepsIt()
    {
        var measured = new ModelCostNotes(Calls: 1, CompletedCalls: 1, InputTokens: 11, OutputTokens: 7);

        Assert.Equal(measured, ModelCostNotes.Add(measured, null));
    }

    [Fact]
    public void Add_OnlyTheRightSideMeasuredAnything_KeepsIt()
    {
        var measured = new ModelCostNotes(Calls: 1, CompletedCalls: 0, InputTokens: 0, OutputTokens: 0);

        Assert.Equal(measured, ModelCostNotes.Add(null, measured));
    }

    // An abandoned attempt and a completed one on the same record: two calls, one of them
    // billed for tokens nobody can see, which is exactly the fact Calls minus CompletedCalls
    // is there to state.
    [Fact]
    public void Add_TwoMeasurements_SumsEveryCountSeparately()
    {
        var abandoned = new ModelCostNotes(Calls: 1, CompletedCalls: 0, InputTokens: 0, OutputTokens: 0);
        var completed = new ModelCostNotes(Calls: 1, CompletedCalls: 1, InputTokens: 11, OutputTokens: 7);

        Assert.Equal(
            new ModelCostNotes(Calls: 2, CompletedCalls: 1, InputTokens: 11, OutputTokens: 7),
            ModelCostNotes.Add(abandoned, completed));
    }

    // One rendering, shared by the Batch complete log line and the scorecard, so the two
    // per-batch surfaces of D61 and D62 can never word the same number differently.
    [Fact]
    public void Describe_NoMeasurement_SaysThereWasNoModelCall()
    {
        Assert.Equal("none", ModelCostNotes.Describe(null));
    }

    [Fact]
    public void Describe_AMeasurement_NamesTheCallsCompletedAndBothTokenCounts()
    {
        var notes = new ModelCostNotes(Calls: 3, CompletedCalls: 1, InputTokens: 11, OutputTokens: 7);

        Assert.Equal("3 call(s), 1 completed, 11 input + 7 output token(s)", ModelCostNotes.Describe(notes));
    }
}
