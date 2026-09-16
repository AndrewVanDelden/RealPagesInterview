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
    [InlineData(SuppressionReason.NoOpAction, "no_op_action")]
    [InlineData(SuppressionReason.DoNotContact, "do_not_contact")]
    [InlineData(SuppressionReason.EscalatedToHuman, "escalated_to_human")]
    public void ToWireName_MatchesSuppressionReasonConverterSpelling(SuppressionReason reason, string expected)
    {
        Assert.Equal(expected, reason.ToWireName());
    }
}
