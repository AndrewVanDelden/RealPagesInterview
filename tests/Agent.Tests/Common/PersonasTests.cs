using Agent.Common;
using Xunit;

namespace Agent.Tests.Common;

// A record's persona is free text, so "prospect" is recognized trimmed and without regard to case,
// and anything else, an absent persona included, is not a prospect.
public class PersonasTests
{
    [Theory]
    [InlineData("prospect", true)]
    [InlineData("  Prospect ", true)]
    [InlineData("PROSPECT", true)]
    [InlineData("resident", false)]
    [InlineData("guarantor", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsProspect_ReadsThePersonaTrimmedAndWithoutCase(string? persona, bool expected)
    {
        Assert.Equal(expected, Personas.IsProspect(persona));
    }
}
