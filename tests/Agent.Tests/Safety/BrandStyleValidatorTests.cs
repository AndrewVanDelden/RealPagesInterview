using Agent.Common;
using Agent.Composition;
using Agent.Domain;
using Agent.Safety;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Safety;

// D42's three brand rules, against the evidence D42 names: the two sample bodies measured as
// they are labeled, and the template composer's own output on both channels in both language
// sets. The three "fails one rule only" tests below are the other half of that evidence: a
// check whose rules cannot fail is the hardcoded true it replaces.
public class BrandStyleValidatorTests
{
    private static NextMessage LabeledMessage(int sampleIndex)
    {
        ExpectedOutcome expected = RealAgentFactory.ReadSampleCases()[sampleIndex].Expected
            ?? throw new InvalidOperationException($"Sample {sampleIndex} carries no label.");

        return expected.NextMessage ?? throw new InvalidOperationException($"Sample {sampleIndex} labels no message.");
    }

    // Sample 1: sms, no subject, one exclamation mark, "Reply STOP to opt out." closes the
    // single-line body.
    [Fact]
    public void Validate_Sample1LabeledMessage_AppliesEveryBrandRule()
    {
        BrandStyleValidationResult result = BrandStyleValidator.Validate(LabeledMessage(0));

        Assert.Empty(result.FailedRules);
        Assert.True(result.Applied);
    }

    // Sample 2: email, a subject, no exclamation mark, and "To opt out of emails, click here
    // or reply STOP." on the last of four lines.
    [Fact]
    public void Validate_Sample2LabeledMessage_AppliesEveryBrandRule()
    {
        BrandStyleValidationResult result = BrandStyleValidator.Validate(LabeledMessage(1));

        Assert.Empty(result.FailedRules);
        Assert.True(result.Applied);
    }

    // D42: the rules are fitted to what the samples prove and they must also hold for the
    // offline composer on every channel and language the sets contain, or the check would
    // fail the program's own output (A13, D26).
    [Theory]
    [InlineData(CommunicationChannel.Sms, "en")]
    [InlineData(CommunicationChannel.Email, "en")]
    [InlineData(CommunicationChannel.Sms, "es")]
    [InlineData(CommunicationChannel.Email, "es")]
    public async Task Validate_TemplateComposerOutput_AppliesEveryBrandRule(CommunicationChannel channel, string language)
    {
        ComposeOutcome outcome = await new TemplateMessageComposer()
            .ComposeAsync(SampleProspectCases.Minimal(language: language), channel);

        ComposedMessage composed = Assert.IsType<ComposeOutcome.Composed>(outcome).Message;
        BrandStyleValidationResult result = BrandStyleValidator.Validate(composed.Message);

        Assert.Empty(result.FailedRules);
    }

    // Rule 1 alone. The message carries an opt-out instruction, so SafetyValidator and the
    // evaluator's OptOut check both pass it; what this rule adds is position, and here the
    // closing line is a call to action instead.
    [Fact]
    public void Validate_OptOutIsNotOnTheLastNonBlankLine_FailsOnlyThatRule()
    {
        var message = new NextMessage(
            CommunicationChannel.Email,
            Subject: "Tour Oak Ridge",
            Body: "Reply STOP to opt out.\nBook your tour today.");

        BrandStyleValidationResult result = BrandStyleValidator.Validate(message);

        Assert.Equal([BrandStyleRule.OptOutOnLastLine], result.FailedRules);
        Assert.False(result.Applied);
    }

    // Rule 2 alone.
    [Fact]
    public void Validate_BodyCarriesTwoExclamationMarks_FailsOnlyThatRule()
    {
        var message = new NextMessage(
            CommunicationChannel.Sms,
            Body: "Hi Taylor! Tours are open! Reply STOP to opt out.");

        BrandStyleValidationResult result = BrandStyleValidator.Validate(message);

        Assert.Equal([BrandStyleRule.ExclamationLimit], result.FailedRules);
    }

    // Rule 3 alone, the sms half: a subject on a channel that has no subject line.
    [Fact]
    public void Validate_SmsCarriesASubject_FailsOnlyThatRule()
    {
        var message = new NextMessage(
            CommunicationChannel.Sms,
            Subject: "Tour Oak Ridge",
            Body: "Reply STOP to opt out.");

        BrandStyleValidationResult result = BrandStyleValidator.Validate(message);

        Assert.Equal([BrandStyleRule.SubjectMatchesChannel], result.FailedRules);
    }

    // Rule 3 alone, the email half. A blank subject is an absent subject: Presence is the one
    // definition of absent for a free-text field, so a composer emitting "   " does not slip
    // past the rule by carrying whitespace.
    [Fact]
    public void Validate_EmailCarriesABlankSubject_FailsOnlyThatRule()
    {
        var message = new NextMessage(
            CommunicationChannel.Email,
            Subject: "   ",
            Body: "Reply STOP to opt out.");

        BrandStyleValidationResult result = BrandStyleValidator.Validate(message);

        Assert.Equal([BrandStyleRule.SubjectMatchesChannel], result.FailedRules);
    }

    // The rule reads the last non-blank line, not the last line: a trailing newline is
    // whitespace, not a missing opt-out.
    [Fact]
    public void Validate_BodyEndsInBlankLines_ReadsTheLastNonBlankLine()
    {
        var message = new NextMessage(
            CommunicationChannel.Sms,
            Body: "Hi Taylor. Reply STOP to opt out.\r\n   \n");

        BrandStyleValidationResult result = BrandStyleValidator.Validate(message);

        Assert.Empty(result.FailedRules);
    }

    // A body with no non-blank line at all has no line for the instruction to sit on, so the
    // position rule fails rather than passing by vacuity.
    [Fact]
    public void Validate_BlankBody_FailsThePositionRule()
    {
        var message = new NextMessage(CommunicationChannel.Sms, Body: "  \n  ");

        BrandStyleValidationResult result = BrandStyleValidator.Validate(message);

        Assert.Equal([BrandStyleRule.OptOutOnLastLine], result.FailedRules);
    }

    // NextMessage.Body is nullable because the suppressed shape on the wire is a null body
    // (D3). Reading it must not throw; a message with no body carries no closing line.
    [Fact]
    public void Validate_NullBody_FailsThePositionRuleWithoutThrowing()
    {
        var message = new NextMessage(CommunicationChannel.Sms);

        BrandStyleValidationResult result = BrandStyleValidator.Validate(message);

        Assert.Equal([BrandStyleRule.OptOutOnLastLine], result.FailedRules);
    }

    // All three at once, so the result is a list of every rule that failed rather than the
    // first one found.
    [Fact]
    public void Validate_MessageFailingEveryRule_ReportsAllThree()
    {
        var message = new NextMessage(
            CommunicationChannel.Email,
            Body: "Great news! Tours are open! Call us today.");

        BrandStyleValidationResult result = BrandStyleValidator.Validate(message);

        Assert.Equal(
            [BrandStyleRule.OptOutOnLastLine, BrandStyleRule.ExclamationLimit, BrandStyleRule.SubjectMatchesChannel],
            result.FailedRules);
    }
}
