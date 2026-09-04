using Agent.Decisions;
using Xunit;

namespace Agent.Tests.Decisions;

public class NextActionPlannerOptionsTests
{
    [Fact]
    public void Constructor_NegativeShortHorizonThresholdDays_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NextActionPlannerOptions(shortHorizonThresholdDays: -1));
    }

    [Fact]
    public void Constructor_NonPositiveLongHorizonFollowUpDays_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NextActionPlannerOptions(longHorizonFollowUpDays: 0));
    }
}
