using Agent.Safety;
using Xunit;

namespace Agent.Tests.Safety;

public class OptOutInstructionsTests
{
    [Theory]
    [InlineData("Reply STOP to opt out.")]
    [InlineData("Responde STOP para cancelar.")]
    [InlineData("Text STOP anytime.")]
    [InlineData("You can opt out at any time.")]
    [InlineData("Opt-out here.")]
    [InlineData("Opt\u2011out here.")]
    [InlineData("Opt\u2010out here.")]
    [InlineData("Opt\u2013out here.")]
    [InlineData("Opt\u2014out here.")]
    [InlineData("Click here to unsubscribe.")]
    public void IsPresent_InstructionInAnyAcceptedForm_True(string text)
    {
        Assert.True(OptOutInstructions.IsPresent(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Sorry, I couldn't find a property near the bus stop you mentioned.")]
    [InlineData("Please stop by the leasing office.")]
    [InlineData("STOPPING by is welcome.")]
    [InlineData("Nonstop fun at the pool.")]
    public void IsPresent_NoInstruction_False(string text)
    {
        Assert.False(OptOutInstructions.IsPresent(text));
    }
}
