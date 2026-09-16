using Agent.Domain;
using Xunit;

namespace Agent.Tests.Domain;

public class ProspectProfileTests
{
    [Fact]
    public void Amenities_NonEmptyList_ReturnsThatList()
    {
        var profile = new ProspectProfile("Taylor", null, ["pool"]);

        Assert.Equal(["pool"], profile.Amenities);
    }

    [Fact]
    public void Amenities_NullList_ReturnsEmptyList()
    {
        var profile = new ProspectProfile("Taylor", null, null);

        Assert.Empty(profile.Amenities);
    }

    [Fact]
    public void Amenities_EmptyButNonNullList_ReturnsEmptyList()
    {
        var profile = new ProspectProfile("Taylor", null, []);

        Assert.Empty(profile.Amenities);
    }

    [Fact]
    public void City_NonEmptyString_ReturnsThatString()
    {
        var profile = new ProspectProfile("Taylor", "Richardson, TX", null);

        Assert.Equal("Richardson, TX", profile.City);
    }

    [Fact]
    public void City_NullString_ReturnsEmptyString()
    {
        var profile = new ProspectProfile("Taylor", null, null);

        Assert.Equal(string.Empty, profile.City);
    }

    [Fact]
    public void City_EmptyButNonNullString_ReturnsEmptyString()
    {
        var profile = new ProspectProfile("Taylor", "", null);

        Assert.Equal(string.Empty, profile.City);
    }

    // The greeting name is the first name without the whitespace, emoji, symbols and invisible
    // characters around it, trimmed by whole grapheme cluster, so an emoji sequence goes as one and an
    // accented letter written as a letter and a combining mark stays whole. What is inside the name is
    // kept, punctuation and markup included; cleaning markup is not this rule.
    [Theory]
    [InlineData("  \U0001F642 Sam \U0001F642 ", "Sam")]
    [InlineData(" Sam‍", "Sam")]
    [InlineData("❤️ Ana", "Ana")]
    [InlineData("Ana️", "Ana")]
    [InlineData("\U0001F468‍\U0001F469‍\U0001F467 Kai", "Kai")]
    [InlineData("\tSam\r\n", "Sam")]
    [InlineData("​Sam﻿", "Sam")]
    [InlineData("́Sam", "Sam")]
    [InlineData("Sam", "Sam")]
    [InlineData("José", "José")]
    [InlineData("O'Neil", "O'Neil")]
    [InlineData("Mary-Jane Jr.", "Mary-Jane Jr.")]
    [InlineData("<b>Dev</b>", "<b>Dev</b>")]
    [InlineData("ليلى", "ليلى")]
    public void GreetingName_FirstNameWithSurroundingSymbols_IsTheNameAlone(string firstName, string expected)
    {
        var profile = new ProspectProfile(firstName);

        Assert.Equal(expected, profile.GreetingName);
    }

    // A lone surrogate is text the JSON reader can accept, and it must not throw: it is trimmed as the
    // replacement character it decodes to. Built in code, since an attribute argument cannot hold one.
    [Fact]
    public void GreetingName_LoneSurrogateBeforeTheName_IsTrimmedWithoutThrowing()
    {
        var profile = new ProspectProfile(new string([(char)0xD83D, 'S', 'a', 'm']));

        Assert.Equal("Sam", profile.GreetingName);
    }

    // A first name that is nothing but whitespace or symbols names no one, so there is no greeting name.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\U0001F642\U0001F642")]
    public void GreetingName_NoLettersOrDigits_IsNull(string? firstName)
    {
        var profile = new ProspectProfile(firstName);

        Assert.Null(profile.GreetingName);
    }
}
