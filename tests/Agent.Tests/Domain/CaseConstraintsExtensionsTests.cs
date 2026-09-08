using Agent.Domain;
using Xunit;

namespace Agent.Tests.Domain;

public class CaseConstraintsExtensionsTests
{
    // D1: an absent constraint is not required, the same "== true" rule SafetyValidator,
    // OpenAiMessageComposer, and Evaluator all apply independently today.
    [Fact]
    public void RequiresOptOutInstructions_Absent_ReturnsFalse()
    {
        var constraints = new CaseConstraints(IncludeOptOutInstructions: null);

        Assert.False(constraints.RequiresOptOutInstructions());
    }

    [Fact]
    public void RequiresOptOutInstructions_ExplicitFalse_ReturnsFalse()
    {
        var constraints = new CaseConstraints(IncludeOptOutInstructions: false);

        Assert.False(constraints.RequiresOptOutInstructions());
    }

    [Fact]
    public void RequiresOptOutInstructions_ExplicitTrue_ReturnsTrue()
    {
        var constraints = new CaseConstraints(IncludeOptOutInstructions: true);

        Assert.True(constraints.RequiresOptOutInstructions());
    }
}
