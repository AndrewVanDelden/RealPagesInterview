using Agent.Domain;
using Xunit;

namespace Agent.Tests.Domain;

// Two actions are equal when every member is, and a mapping is compared by its entries rather
// than by reference, so an action built twice from the same values is the same action.
public class NextActionTests
{
    private static readonly NextAction Baseline = new("schedule_sms_reminder", "renewal", 3, "reason", 5, new Dictionary<string, string> { ["yes"] = "a", ["no"] = "b" });

    // Each case differs from the baseline in exactly one member.
    private static readonly NextAction[] OneMemberDiffers =
    [
        Baseline with { Type = "follow_up_in_days" },
        Baseline with { Name = "other" },
        Baseline with { Value = 4 },
        Baseline with { Reason = "other" },
        Baseline with { InDays = 6 },
        Baseline with { Mapping = null },
        Baseline with { Mapping = new Dictionary<string, string> { ["yes"] = "a", ["maybe"] = "b" } },
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Equals_OneMemberDiffers_IsNotEqual(int caseIndex)
    {
        Assert.NotEqual(Baseline, OneMemberDiffers[caseIndex]);
    }

    // Equal actions hash equally, with a mapping or without one, so an action can key a lookup.
    [Fact]
    public void GetHashCode_EqualActions_HashEqually()
    {
        Assert.Equal(new NextAction("follow_up_in_days", Value: 2).GetHashCode(), new NextAction("follow_up_in_days", Value: 2).GetHashCode());
        Assert.Equal(Baseline.GetHashCode(), (Baseline with { Mapping = new Dictionary<string, string> { ["no"] = "b", ["yes"] = "a" } }).GetHashCode());
    }

    // An action with no mapping is not equal to one with a mapping, whichever side it is on.
    [Fact]
    public void Equals_OnlyTheOtherActionHasAMapping_IsNotEqual()
    {
        Assert.NotEqual(Baseline with { Mapping = null }, Baseline);
    }
}
