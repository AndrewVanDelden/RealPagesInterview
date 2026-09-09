using Agent.Domain;
using Agent.Safety;
using Xunit;

namespace Agent.Tests.Safety;

public class SafetyValidatorTests
{
    private static readonly ISafetyValidator Validator = new SafetyValidator();

    private static CaseConstraints Constraints(bool? noPiiLeak = true, bool? includeOptOutInstructions = true) =>
        new(noPiiLeak, NoSensitiveDiscrimination: null, includeOptOutInstructions, PrimaryCta: "book_tour");

    private static NextMessage Message(string? body, string? subject = null, CommunicationChannel channel = CommunicationChannel.Sms) =>
        new(channel, null, subject, body, null);

    // D1: an absent constraint is not required, so the two gated checks report
    // NotApplicable rather than a pass (D38). The two unconditional checks still run.
    [Fact]
    public void Validate_NoConstraintsStated_GatedChecksAreNotApplicableAndUnconditionalOnesRun()
    {
        SafetyValidationResult result = Validator.Validate(Message("Hi Taylor! Book a tour today, families only."), new CaseConstraints());

        Assert.Equal(SafetyCheckVerdict.NotApplicable, result.OptOutInstructions.Verdict);
        Assert.Equal(SafetyCheckVerdict.NotApplicable, result.LongDigitRun.Verdict);
        Assert.Equal(SafetyCheckVerdict.Passed, result.SocialSecurityNumber.Verdict);
        Assert.Equal(SafetyCheckVerdict.Failed, result.FairHousing.Verdict);

        string violation = Assert.Single(result.Violations);
        Assert.Contains("families only", violation);
    }

    // D38's named defect: FairHousingCheckPassed was violations.Count == 0, so a message
    // that only omitted its opt-out line recorded a fair-housing failure that never
    // happened. Each check now answers for itself.
    [Fact]
    public void Validate_OnlyTheOptOutLineIsMissing_FairHousingStillPasses()
    {
        SafetyValidationResult result = Validator.Validate(Message("Hi Taylor! Book a tour today."), Constraints());

        Assert.Equal(SafetyCheckVerdict.Failed, result.OptOutInstructions.Verdict);
        Assert.Equal(SafetyCheckVerdict.Passed, result.FairHousing.Verdict);
        Assert.Single(result.Violations);
    }

    // Body is nullable because the oracle's suppressed shape carries a null body
    // (retrospective D3). A null body validates as empty text: no opt-out, no PII.
    [Fact]
    public void Validate_NullBody_ValidatesAsEmptyText()
    {
        SafetyValidationResult result = Validator.Validate(Message(null), Constraints());

        Assert.Single(result.Violations);
        Assert.Contains("opt-out", result.Violations[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_CleanMessageWithOptOut_EveryCheckPasses()
    {
        SafetyValidationResult result = Validator.Validate(Message("Hi Taylor! Book a tour. Reply STOP to opt out."), Constraints());

        Assert.Empty(result.Violations);
        Assert.All(result.Checks, check => Assert.Equal(SafetyCheckVerdict.Passed, check.Verdict));
    }

    [Fact]
    public void Validate_OptOutNotRequired_MissingOptOutIsNotApplicable()
    {
        SafetyValidationResult result = Validator.Validate(Message("Hi Taylor! Book a tour today."), Constraints(includeOptOutInstructions: false));

        Assert.Empty(result.Violations);
        Assert.Equal(SafetyCheckVerdict.NotApplicable, result.OptOutInstructions.Verdict);
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
        Assert.Equal(SafetyCheckVerdict.Failed, result.FairHousing.Verdict);
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
        Assert.Equal(SafetyCheckVerdict.Failed, result.SocialSecurityNumber.Verdict);
    }

    [Fact]
    public void Validate_FormattedLongDigitRun_YieldsPiiViolation()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("Your card 4111-1111-1111-1111 was charged. Reply STOP to opt out."),
            Constraints());

        Assert.Equal(SafetyCheckVerdict.Failed, result.LongDigitRun.Verdict);
        Assert.Contains(result.Violations, v => v.Contains("identifier", StringComparison.OrdinalIgnoreCase));
    }

    // A sixteen-digit card matches the long-digit run and nothing else: the Social
    // Security pattern needs a word boundary after exactly nine digits, so the two
    // checks can never claim the same span and the count stays one.
    [Fact]
    public void Validate_SixteenDigitRun_IsOwnedByLongDigitRunAloneAndNotDoubleCounted()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("Your card 4111-1111-1111-1111 was charged. Reply STOP to opt out."),
            Constraints());

        Assert.Equal(SafetyCheckVerdict.Passed, result.SocialSecurityNumber.Verdict);
        Assert.Single(result.Violations);
    }

    // A Social Security number is nine digits, which is below the long-digit run's
    // thirteen-digit floor, so it is one violation and not two.
    [Fact]
    public void Validate_SocialSecurityNumber_IsOwnedBySsnCheckAloneAndNotDoubleCounted()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("Your SSN 123-45-6789 is on file. Reply STOP to opt out."),
            Constraints());

        Assert.Equal(SafetyCheckVerdict.Passed, result.LongDigitRun.Verdict);
        Assert.Single(result.Violations);
    }

    // D40, and the deliberate inversion of the recorded behavior this test used to assert.
    // no_pii_leak: false says the record does not need the heuristic, not that it consents
    // to a Social Security number leaving in a message.
    [Fact]
    public void Validate_PiiCheckNotRequired_SocialSecurityNumberIsStillAViolation()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("Your SSN 123-45-6789 is on file. Reply STOP to opt out."),
            Constraints(noPiiLeak: false));

        Assert.Equal(SafetyCheckVerdict.Failed, result.SocialSecurityNumber.Verdict);
        Assert.Single(result.Violations);
    }

    // D40: the long-digit run stays gated, because it is a proxy that also matches a
    // confirmation number or a tour reference, and a record with a legitimate long
    // identifier needs a way to say so.
    [Fact]
    public void Validate_PiiCheckNotRequired_LongDigitRunIsNotApplicable()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("Your card 4111-1111-1111-1111 was charged. Reply STOP to opt out."),
            Constraints(noPiiLeak: false));

        Assert.Equal(SafetyCheckVerdict.NotApplicable, result.LongDigitRun.Verdict);
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

    // D38's second named defect: FindWholeWord was FirstOrDefault, so a message matching
    // six protected-class terms emitted exactly one violation and safety_violations_max
    // was scored against that count.
    [Fact]
    public void Validate_MessageMatchingSixTerms_ReportsEveryDistinctTerm()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("We steer applicants by race, color, religion, national origin, familial status, and disability. Reply STOP to opt out."),
            Constraints());

        Assert.Equal(6, result.FairHousing.Details.Count);
        Assert.Equal(6, result.Violations.Count);
    }

    // The same term twice is the same fact twice, not two findings.
    [Fact]
    public void Validate_SameTermTwice_ReportsItOnce()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("No kids here, and we mean no kids. Reply STOP to opt out."),
            Constraints());

        Assert.Single(result.FairHousing.Details);
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

    [Fact]
    public void Validate_Checks_CarriesTheFourChecksInDecisionOrder()
    {
        SafetyValidationResult result = Validator.Validate(Message("Hi Taylor! Reply STOP to opt out."), Constraints());

        Assert.Equal(
            [SafetyCheck.OptOutInstructions, SafetyCheck.SocialSecurityNumber, SafetyCheck.LongDigitRun, SafetyCheck.FairHousing],
            result.Checks.Select(check => check.Check));
    }

    [Fact]
    public void VerdictOf_ReadsOneChecksVerdict()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("This community is families only.", subject: null),
            Constraints(noPiiLeak: false));

        Assert.Equal(SafetyCheckVerdict.Failed, result.VerdictOf(SafetyCheck.OptOutInstructions));
        Assert.Equal(SafetyCheckVerdict.Passed, result.VerdictOf(SafetyCheck.SocialSecurityNumber));
        Assert.Equal(SafetyCheckVerdict.NotApplicable, result.VerdictOf(SafetyCheck.LongDigitRun));
        Assert.Equal(SafetyCheckVerdict.Failed, result.VerdictOf(SafetyCheck.FairHousing));
    }

    // The flat list is what the compose-validate loop feeds back and what the agent
    // counts, so it is the failed checks' details in check order and nothing else.
    [Fact]
    public void Validate_Violations_AreTheFailedChecksDetailsInCheckOrder()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("Your SSN 123-45-6789 is on file, families only."),
            Constraints());

        Assert.Equal(3, result.Violations.Count);
        Assert.Contains("opt-out", result.Violations[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Social Security", result.Violations[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("families only", result.Violations[2]);
    }

    [Fact]
    public void Validate_PassedCheck_CarriesNoDetails()
    {
        SafetyValidationResult result = Validator.Validate(Message("Hi Taylor! Reply STOP to opt out."), Constraints());

        Assert.Empty(result.FairHousing.Details);
    }

    [Fact]
    public void Validate_NotApplicableCheck_CarriesNoDetails()
    {
        SafetyValidationResult result = Validator.Validate(Message("Hi Taylor!"), Constraints(noPiiLeak: false, includeOptOutInstructions: false));

        Assert.Empty(result.OptOutInstructions.Details);
        Assert.Empty(result.LongDigitRun.Details);
    }
}
