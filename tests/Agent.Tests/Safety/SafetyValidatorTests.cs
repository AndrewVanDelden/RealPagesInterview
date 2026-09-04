using Agent.Domain;
using Agent.Safety;
using Xunit;

namespace Agent.Tests.Safety;

public class SafetyValidatorTests
{
    private static readonly ISafetyValidator Validator = new SafetyValidator();

    private static CaseConstraints Constraints(bool noPiiLeak = true, bool includeOptOutInstructions = true) =>
        new(noPiiLeak, NoSensitiveDiscrimination: null, includeOptOutInstructions, PrimaryCta: "book_tour");

    private static NextMessage Message(string body) => new(CommunicationChannel.Sms, null, null, body, null);

    [Fact]
    public void Validate_CleanMessageWithOptOut_ReturnsZeroViolationsAndPassed()
    {
        ValidationResult result = Validator.Validate(Message("Hi Taylor! Book a tour. Reply STOP to opt out."), Constraints());

        Assert.Empty(result.Violations);
        Assert.True(result.FairHousingCheckPassed);
    }

    [Fact]
    public void Validate_MissingOptOutWhenRequired_YieldsViolation()
    {
        ValidationResult result = Validator.Validate(Message("Hi Taylor! Book a tour today."), Constraints());

        Assert.Single(result.Violations);
        Assert.False(result.FairHousingCheckPassed);
    }

    [Fact]
    public void Validate_OptOutNotRequired_MissingOptOutIsNotAViolation()
    {
        ValidationResult result = Validator.Validate(Message("Hi Taylor! Book a tour today."), Constraints(includeOptOutInstructions: false));

        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Validate_SteeringPhrase_YieldsViolation()
    {
        ValidationResult result = Validator.Validate(
            Message("This community is families only. Reply STOP to opt out."),
            Constraints());

        Assert.Contains(result.Violations, v => v.Contains("protected", StringComparison.OrdinalIgnoreCase));
        Assert.False(result.FairHousingCheckPassed);
    }

    [Fact]
    public void Validate_PiiLeak_YieldsViolation()
    {
        ValidationResult result = Validator.Validate(
            Message("Your SSN 123-45-6789 is on file. Reply STOP to opt out."),
            Constraints());

        Assert.Contains(result.Violations, v => v.Contains("identifier", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_PiiCheckNotRequired_LeakedIdentifierIsNotAViolation()
    {
        ValidationResult result = Validator.Validate(
            Message("Your SSN 123-45-6789 is on file. Reply STOP to opt out."),
            Constraints(noPiiLeak: false));

        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Validate_MultipleViolations_CountsAll()
    {
        ValidationResult result = Validator.Validate(
            Message("This community is families only. Your SSN 123-45-6789 is on file."),
            Constraints());

        Assert.Equal(3, result.Violations.Count);
    }
}
