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
}
