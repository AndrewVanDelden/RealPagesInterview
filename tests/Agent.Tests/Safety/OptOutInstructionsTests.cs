using Agent.Safety;
using Xunit;

namespace Agent.Tests.Safety;

public class OptOutInstructionsTests
{
    [Theory]
    [InlineData("Reply STOP to opt out.")]
    [InlineData("Responde STOP para cancelar.")]
    [InlineData("Text STOP anytime.")]
    [InlineData("Reply stop to end texts.")]
    [InlineData("You can text stop anytime.")]
    [InlineData("You can opt out at any time.")]
    [InlineData("Opt-out here.")]
    [InlineData("Opt\u2011out here.")]
    [InlineData("Opt\u2010out here.")]
    [InlineData("Opt\u2013out here.")]
    [InlineData("Opt\u2014out here.")]
    [InlineData("Click here to unsubscribe.")]
    [InlineData("Hours at https://oakridge.example/hours. Reply STOP to opt out.")]
    [InlineData("Details at https://oakridge.example/info. STOP to end texts.")]
    public void IsPresent_InstructionInAnyAcceptedForm_True(string text)
    {
        Assert.True(OptOutInstructions.IsPresent(text));
    }

    // The keyword inside a URL path is not an instruction the recipient can act on,
    // and because this is the one definition the scorer uses too, the false pass
    // propagated into the scorecard. URL spans are removed before the keyword scan only.
    [Theory]
    [InlineData("Visit https://oakridge.example/STOP-by-today for hours.")]
    [InlineData("See http://oakridge.example/STOP for details.")]
    [InlineData("Tour times: https://oakridge.example/tours/STOP")]
    public void IsPresent_KeywordOnlyInsideAUrl_False(string text)
    {
        Assert.False(OptOutInstructions.IsPresent(text));
    }

    // The phrase check is deliberately left as it is (the URL fix is scoped to the bare
    // keyword), so a URL whose path spells the phrase still counts. Pinned as the
    // remaining hole rather than left undocumented.
    [Fact]
    public void IsPresent_PhraseOnlyInsideAUrl_StillTrue()
    {
        Assert.True(OptOutInstructions.IsPresent("Visit https://oakridge.example/opt-out for details."));
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
