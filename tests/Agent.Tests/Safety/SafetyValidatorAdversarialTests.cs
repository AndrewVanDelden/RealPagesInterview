using Agent.Domain;
using Agent.Safety;
using Xunit;

namespace Agent.Tests.Safety;

// Playbook step 70: for each check, the obvious violation, a paraphrase, and an evasion
// hidden in formatting. Where this keyword-and-pattern proxy does not catch the case, the
// test asserts the miss rather than a fix that does not exist. Every such test names the
// scope-out it belongs to: semantic paraphrase is the limitation recorded in
// docs/CODE_REVIEW.md, and letter spacing, interior punctuation and homoglyphs are the
// three formatting evasions deliberately left unfixed, each for the reason its test states.
public class SafetyValidatorAdversarialTests
{
    private static readonly ISafetyValidator Validator = new SafetyValidator();

    private static NextMessage Message(string? body) => new(CommunicationChannel.Sms, null, null, body, null);

    private static SafetyCheckVerdict Verdict(string body, SafetyCheck check, CaseConstraints constraints) =>
        Validator.Validate(Message(body), constraints).VerdictOf(check);

    private static readonly CaseConstraints OptOutRequired = new(IncludeOptOutInstructions: true);
    private static readonly CaseConstraints PiiChecked = new(NoPiiLeak: true);
    private static readonly CaseConstraints NothingStated = new();

    // --- SafetyCheck.OptOutInstructions ---

    [Fact]
    public void OptOut_ObviousViolation_NoInstructionAtAll_Fails()
    {
        Assert.Equal(SafetyCheckVerdict.Failed, Verdict("Book a tour at Oak Ridge today.", SafetyCheck.OptOutInstructions, OptOutRequired));
    }

    // Paraphrase, not caught: the proxy recognises the carrier keyword and three fixed
    // phrases, so a message that offers a real opt-out in its own words is rejected as if
    // it carried none. Over-blocking, and the same semantic scope-out docs/CODE_REVIEW.md
    // records for the steering check.
    [Theory]
    [InlineData("Tell us any time and we will stop messaging you.")]
    [InlineData("Let us know if you would rather not hear from us again.")]
    public void OptOut_Paraphrase_IsNotRecognisedAndTheMessageFails(string body)
    {
        Assert.Equal(SafetyCheckVerdict.Failed, Verdict(body, SafetyCheck.OptOutInstructions, OptOutRequired));
    }

    // Evasion in formatting, caught: the keyword hidden inside a URL path is not an
    // instruction the recipient can act on, and because OptOutInstructions is the one
    // definition the scorer uses too, the false pass propagated into the scorecard.
    [Fact]
    public void OptOut_KeywordHiddenInsideAUrl_Fails()
    {
        Assert.Equal(
            SafetyCheckVerdict.Failed,
            Verdict("Visit https://oakridge.example/STOP-by-today for hours.", SafetyCheck.OptOutInstructions, OptOutRequired));
    }

    // --- SafetyCheck.SocialSecurityNumber ---

    [Fact]
    public void Ssn_ObviousViolation_HyphenatedNumber_Fails()
    {
        Assert.Equal(SafetyCheckVerdict.Failed, Verdict("Your SSN 123-45-6789 is on file.", SafetyCheck.SocialSecurityNumber, NothingStated));
    }

    // Paraphrase, not caught: a number spelled out in words is not a digit pattern. Same
    // semantic scope-out as above.
    [Fact]
    public void Ssn_NumberSpelledOutInWords_IsNotCaught()
    {
        Assert.Equal(
            SafetyCheckVerdict.Passed,
            Verdict("My social is one two three four five six seven eight nine.", SafetyCheck.SocialSecurityNumber, NothingStated));
    }

    // Evasion in formatting, caught: the pattern was hyphens only, so a number
    // written with spaces or bare missed entirely.
    [Theory]
    [InlineData("Your SSN 123 45 6789 is on file.")]
    [InlineData("Your SSN 123456789 is on file.")]
    public void Ssn_SpacedOrBareNineDigits_Fails(string body)
    {
        Assert.Equal(SafetyCheckVerdict.Failed, Verdict(body, SafetyCheck.SocialSecurityNumber, NothingStated));
    }

    // Evasion in formatting, not caught: interior punctuation, one of the three
    // deliberately-not-fixed evasions. Matching across arbitrary separators trades these
    // misses for a larger false-positive class.
    [Fact]
    public void Ssn_InteriorPunctuationBetweenGroups_IsNotCaught()
    {
        Assert.Equal(SafetyCheckVerdict.Passed, Verdict("Your SSN 123.45.6789 is on file.", SafetyCheck.SocialSecurityNumber, NothingStated));
    }

    // The inversion of the test that recorded the defect. The widened pattern first
    // shipped as \b\d{3}[- ]?\d{2}[- ]?\d{4}\b, which reads a ZIP+4 as three digits, two
    // digits and four digits by taking the first separator as absent and the second as
    // present. The grouping is consistent now (hyphens throughout, spaces throughout, or nine
    // bare digits), so a five-and-four pair is not a shape the pattern describes and a property
    // address stops being suppressed by a gate no record can switch off.
    [Theory]
    [InlineData("Send mail to Oak Ridge, TX 75201-1234.")]
    [InlineData("Our office is at 12345-6789.")]
    public void Ssn_ZipPlusFour_IsNotAViolation(string body)
    {
        Assert.Equal(SafetyCheckVerdict.Passed, Verdict(body, SafetyCheck.SocialSecurityNumber, NothingStated));
    }

    // A bare nine-digit confirmation number is the same false positive as the
    // fourteen-digit one the long-digit run exempts, so the same span
    // exempts it here. The introducer still does not reach across a sentence terminator.
    [Fact]
    public void Ssn_IntroducedConfirmationNumber_IsNotAViolation()
    {
        Assert.Equal(SafetyCheckVerdict.Passed, Verdict("Your confirmation number 123456789 is ready.", SafetyCheck.SocialSecurityNumber, NothingStated));
    }

    [Fact]
    public void Ssn_ConfirmationInAnEarlierSentence_DoesNotExemptTheNextSentencesNumber()
    {
        Assert.Equal(SafetyCheckVerdict.Failed, Verdict("Your confirmation is ready. Your SSN 123-45-6789 is on file.", SafetyCheck.SocialSecurityNumber, NothingStated));
    }

    // The introducer span requires a word boundary before "reference": without one, it
    // matched starting inside "preference" (p-REFERENCE) and the digits that followed were
    // exempted as if a confirmation number had introduced them.
    [Fact]
    public void Ssn_WordReferenceInsideALongerWord_DoesNotExemptTheFollowingNumber()
    {
        Assert.Equal(
            SafetyCheckVerdict.Failed,
            Verdict("Your preference and SSN 123-45-6789 are noted.", SafetyCheck.SocialSecurityNumber, NothingStated));
    }

    // --- SafetyCheck.LongDigitRun ---

    [Fact]
    public void LongDigitRun_ObviousViolation_SixteenBareDigits_Fails()
    {
        Assert.Equal(SafetyCheckVerdict.Failed, Verdict("Your card 4111111111111111 was charged.", SafetyCheck.LongDigitRun, PiiChecked));
    }

    // Paraphrase, not caught: no digits, no pattern.
    [Fact]
    public void LongDigitRun_NumberDescribedInWords_IsNotCaught()
    {
        Assert.Equal(
            SafetyCheckVerdict.Passed,
            Verdict("Your card ending in one one one one was charged.", SafetyCheck.LongDigitRun, PiiChecked));
    }

    // Evasion in formatting, caught: the pattern is tolerant of the space and dash
    // grouping real formatted numbers use.
    [Theory]
    [InlineData("Your card 4111 1111 1111 1111 was charged.")]
    [InlineData("Your card 4111-1111-1111-1111 was charged.")]
    public void LongDigitRun_GroupedBySpacesOrDashes_Fails(string body)
    {
        Assert.Equal(SafetyCheckVerdict.Failed, Verdict(body, SafetyCheck.LongDigitRun, PiiChecked));
    }

    // Evasion in formatting, not caught: the format-character strip applies to the
    // copy of the text used for term matching only, so a zero-width space splits the run
    // into two shorter ones and the digit patterns still miss it.
    [Fact]
    public void LongDigitRun_ZeroWidthSpaceInsideTheRun_IsNotCaught()
    {
        Assert.Equal(SafetyCheckVerdict.Passed, Verdict("Your card 4111\u200b111111111111 was charged.", SafetyCheck.LongDigitRun, PiiChecked));
    }

    // --- SafetyCheck.FairHousing ---

    [Fact]
    public void FairHousing_ObviousViolation_SteeringPhrase_Fails()
    {
        Assert.Equal(SafetyCheckVerdict.Failed, Verdict("This community is families only.", SafetyCheck.FairHousing, NothingStated));
    }

    // Paraphrase, not caught. This is the scope-out recorded in docs/CODE_REVIEW.md under
    // "Fair-housing/PII heuristic, not semantic understanding": catching these needs
    // understanding rather than a pattern. Pinned as a miss, not asserted as a catch.
    [Theory]
    [InlineData("We prefer residents without young children.")]
    [InlineData("This building suits mature professionals, not growing families.")]
    public void FairHousing_SteeringParaphrase_IsNotCaught(string body)
    {
        Assert.Equal(SafetyCheckVerdict.Passed, Verdict(body, SafetyCheck.FairHousing, NothingStated));
    }

    // Evasion in formatting, caught. None of these needs an adversary: a hyphen and
    // a double space are ordinary prose, and a zero-width space arrives by copy and paste.
    [Theory]
    [InlineData("This community is families-only.")]
    [InlineData("This community is families  only.")]
    [InlineData("This community is famil\u200bies only.")]
    [InlineData("This community is families\u2011only.")]
    public void FairHousing_TermSplitByFormatting_Fails(string body)
    {
        Assert.Equal(SafetyCheckVerdict.Failed, Verdict(body, SafetyCheck.FairHousing, NothingStated));
    }

    // Evasion in formatting, not caught: letter spacing and Cyrillic homoglyphs, two of the
    // three deliberately-not-fixed evasions. Letter spacing would make short
    // terms fire on unrelated letter sequences; the homoglyph threat model does not apply,
    // because the text under validation is written by this system's own composer (A18).
    [Theory]
    [InlineData("This community is f a m i l i e s  o n l y.")]
    [InlineData("This community is famili\u0435s only.")]
    public void FairHousing_LetterSpacingOrHomoglyph_IsNotCaught(string body)
    {
        Assert.Equal(SafetyCheckVerdict.Passed, Verdict(body, SafetyCheck.FairHousing, NothingStated));
    }
}
