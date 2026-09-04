using Agent.Domain;
using Xunit;

namespace Agent.Tests.Domain;

public class ProspectProfileTests
{
    [Fact]
    public void HasAmenityInterest_NonEmptyList_ReturnsTrue()
    {
        var profile = new ProspectProfile("Taylor", null, ["pool"]);

        Assert.True(profile.HasAmenityInterest);
    }

    [Fact]
    public void HasAmenityInterest_NullOrEmptyList_ReturnsFalse()
    {
        var nullProfile = new ProspectProfile("Taylor", null, null);
        var emptyProfile = new ProspectProfile("Taylor", null, []);

        Assert.False(nullProfile.HasAmenityInterest);
        Assert.False(emptyProfile.HasAmenityInterest);
    }

    [Fact]
    public void HasCityInterest_NonEmptyString_ReturnsTrue()
    {
        var profile = new ProspectProfile("Taylor", "Richardson, TX", null);

        Assert.True(profile.HasCityInterest);
    }

    [Fact]
    public void HasCityInterest_NullOrEmptyString_ReturnsFalse()
    {
        var nullProfile = new ProspectProfile("Taylor", null, null);
        var emptyProfile = new ProspectProfile("Taylor", "", null);

        Assert.False(nullProfile.HasCityInterest);
        Assert.False(emptyProfile.HasCityInterest);
    }
}
