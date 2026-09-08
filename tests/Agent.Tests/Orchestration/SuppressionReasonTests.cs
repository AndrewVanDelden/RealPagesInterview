using Agent.Orchestration;
using Xunit;

namespace Agent.Tests.Orchestration;

public class SuppressionReasonTests
{
    [Theory]
    [InlineData(SuppressionReason.None, "none")]
    [InlineData(SuppressionReason.NoContactConsent, "no_contact_consent")]
    [InlineData(SuppressionReason.CompositionFailed, "composition_failed")]
    [InlineData(SuppressionReason.SafetyViolation, "safety_violation")]
    public void ToWireName_MatchesSuppressionReasonConverterSpelling(SuppressionReason reason, string expected)
    {
        Assert.Equal(expected, reason.ToWireName());
    }
}
