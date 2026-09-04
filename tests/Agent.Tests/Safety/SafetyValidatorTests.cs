using Agent.Domain;
using Agent.Safety;
using Xunit;

namespace Agent.Tests.Safety;

public class SafetyValidatorTests
{
    private static readonly ISafetyValidator Validator = new SafetyValidator();

    private static CaseConstraints Constraints(bool noPiiLeak = true, bool includeOptOutInstructions = true) =>
        new(noPiiLeak, NoSensitiveDiscrimination: null, includeOptOutInstructions, PrimaryCta: "book_tour");

    private static NextMessage Message(string body, string? subject = null, CommunicationChannel channel = CommunicationChannel.Sms) =>
        new(channel, null, subject, body, null);

    [Fact]
    public void Validate_CleanMessageWithOptOut_ReturnsZeroViolationsAndPassed()
    {
        SafetyValidationResult result = Validator.Validate(Message("Hi Taylor! Book a tour. Reply STOP to opt out."), Constraints());

        Assert.Empty(result.Violations);
        Assert.True(result.FairHousingCheckPassed);
    }

    [Fact]
    public void Validate_MissingOptOutWhenRequired_YieldsViolation()
    {
        SafetyValidationResult result = Validator.Validate(Message("Hi Taylor! Book a tour today."), Constraints());

        Assert.Single(result.Violations);
        Assert.False(result.FairHousingCheckPassed);
    }

    [Fact]
    public void Validate_OptOutNotRequired_MissingOptOutIsNotAViolation()
    {
        SafetyValidationResult result = Validator.Validate(Message("Hi Taylor! Book a tour today."), Constraints(includeOptOutInstructions: false));

        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Validate_BareStopSubstringWithoutRealOptOutLanguage_StillYieldsMissingOptOutViolation()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("Sorry, I couldn't find a property near the bus stop you mentioned."),
            Constraints());

        Assert.Contains(result.Violations, v => v.Contains("opt-out", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_SteeringPhrase_YieldsViolation()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("This community is families only. Reply STOP to opt out."),
            Constraints());

        Assert.Contains(result.Violations, v => v.Contains("protected", StringComparison.OrdinalIgnoreCase));
        Assert.False(result.FairHousingCheckPassed);
    }

    [Fact]
    public void Validate_WordContainingSteeringSubstring_IsNotAFalsePositive()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("We heard you're looking in Colorado Springs, CO. Reply STOP to opt out."),
            Constraints());

        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Validate_PiiLeak_YieldsViolation()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("Your SSN 123-45-6789 is on file. Reply STOP to opt out."),
            Constraints());

        Assert.Contains(result.Violations, v => v.Contains("identifier", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FormattedLongDigitRun_YieldsPiiViolation()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("Your card 4111-1111-1111-1111 was charged. Reply STOP to opt out."),
            Constraints());

        Assert.Contains(result.Violations, v => v.Contains("identifier", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_PiiCheckNotRequired_LeakedIdentifierIsNotAViolation()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("Your SSN 123-45-6789 is on file. Reply STOP to opt out."),
            Constraints(noPiiLeak: false));

        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Validate_MultipleViolations_CountsAll()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("This community is families only. Your SSN 123-45-6789 is on file."),
            Constraints());

        Assert.Equal(3, result.Violations.Count);
    }

    [Fact]
    public void Validate_CleanBodyWithSteeringSubject_YieldsViolation()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("Book a tour today. Reply STOP to opt out.", subject: "Tour Oak Ridge - Families Only", channel: CommunicationChannel.Email),
            Constraints());

        Assert.Contains(result.Violations, v => v.Contains("protected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_CleanBodyWithPiiInSubject_YieldsViolation()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("Book a tour today. Reply STOP to opt out.", subject: "Confirming SSN 123-45-6789", channel: CommunicationChannel.Email),
            Constraints());

        Assert.Contains(result.Violations, v => v.Contains("identifier", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NullSubject_DoesNotThrow()
    {
        SafetyValidationResult result = Validator.Validate(Message("Hi Taylor! Reply STOP to opt out."), Constraints());

        Assert.Empty(result.Violations);
    }
}
