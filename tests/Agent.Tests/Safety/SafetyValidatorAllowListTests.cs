using Agent.Domain;
using Agent.Safety;
using Xunit;

namespace Agent.Tests.Safety;

// Playbook step 63, D41: the allow-list is a span list, never a term list, so every row
// is tested in both directions. The exempt span yields no violation, and the same bare
// term in a steering sentence still yields one, which is what proves the term stayed live
// instead of being switched off across the whole message.
public class SafetyValidatorAllowListTests
{
    private static readonly ISafetyValidator Validator = new SafetyValidator();

    // Every constraint absent, so the two gated checks report NotApplicable and the count
    // below is the fair-housing check's own answer and nothing else (D1, D38).
    private static readonly CaseConstraints FairHousingOnly = new();

    private static NextMessage Message(string body) => new(CommunicationChannel.Sms, null, null, body, null);

    private static SafetyValidationResult Validate(string body) => Validator.Validate(Message(body), FairHousingOnly);

    // The single most important finding of the D41 probe: the disclosure a compliant
    // leasing message is expected to carry matches six terms and is suppressed, so the
    // proxy blocks the compliant message and passes nothing in its place.
    [Theory]
    [InlineData("We are an equal housing opportunity provider. We do not discriminate on the basis of race, color, religion, national origin, familial status, disability, or sex.")]
    [InlineData("Oak Ridge is an equal housing opportunity provider and does not discriminate based on race, color, religion, national origin, familial status, disability, or sex.")]
    [InlineData("We do not discriminate against any applicant on the basis of race, color, religion, national origin, familial status, or disability.")]
    public void Validate_EqualHousingOpportunityDisclosure_YieldsNoViolation(string body)
    {
        Assert.Empty(Validate(body).Violations);
    }

    // The exempt span ends at the sentence, so a steering sentence that follows the
    // disclosure is still read.
    [Fact]
    public void Validate_DisclosureFollowedByASteeringSentence_StillYieldsTheSteeringViolation()
    {
        SafetyValidationResult result = Validate(
            "We do not discriminate on the basis of race, color, religion, national origin, familial status, disability, or sex. This community is families only.");

        string violation = Assert.Single(result.Violations);
        Assert.Contains("families only", violation);
    }

    // The exempt span must end at a clause boundary, not only a sentence terminator: a
    // steering clause tacked onto the disclosure with a comma and a contrastive conjunction
    // is not part of the disclosure and must still be read.
    [Fact]
    public void Validate_DisclosureFollowedByASteeringClauseInTheSameSentence_StillYieldsTheSteeringViolation()
    {
        SafetyValidationResult result = Validate("We do not discriminate, but this community is families only.");

        string violation = Assert.Single(result.Violations);
        Assert.Contains("families only", violation);
    }

    // Normalization collapses a newline into a space before the exempt span runs, so a
    // disclosure on its own line with no terminal period must not fuse with the sentence
    // that follows it on the next line.
    [Fact]
    public void Validate_DisclosureFollowedByASteeringSentenceOnANewLine_StillYieldsTheSteeringViolation()
    {
        SafetyValidationResult result = Validate("We do not discriminate\nThis community is families only.");

        string violation = Assert.Single(result.Violations);
        Assert.Contains("families only", violation);
    }

    [Fact]
    public void Validate_DisabilityAccommodationsOffer_YieldsNoViolation()
    {
        Assert.Empty(Validate("We offer disability accommodations on request.").Violations);
    }

    [Fact]
    public void Validate_BareDisabilityInASteeringSentence_StillYieldsOneViolation()
    {
        string violation = Assert.Single(Validate("We do not rent to anyone with a disability.").Violations);

        Assert.Contains("disability", violation);
    }

    // This row removes nothing against the current term list: no protected-class or
    // steering term matches "wheelchair accessible". It is pinned here so the row's
    // effect is a recorded fact rather than an assumption.
    [Fact]
    public void Validate_WheelchairAccessibleFeature_YieldsNoViolation()
    {
        Assert.Empty(Validate("Every home is wheelchair accessible.").Violations);
    }

    [Fact]
    public void Validate_ColorSchemeDescription_YieldsNoViolation()
    {
        Assert.Empty(Validate("The color scheme is warm neutrals throughout.").Violations);
    }

    [Fact]
    public void Validate_BareColorInASteeringSentence_StillYieldsOneViolation()
    {
        string violation = Assert.Single(Validate("We choose residents by color.").Violations);

        Assert.Contains("color", violation);
    }

    [Theory]
    [InlineData("Join our 5K race on Saturday morning.")]
    [InlineData("Join our 10k race on Saturday morning.")]
    public void Validate_CharityRunEvent_YieldsNoViolation(string body)
    {
        Assert.Empty(Validate(body).Violations);
    }

    [Fact]
    public void Validate_RaceSimulatorAmenity_YieldsNoViolation()
    {
        Assert.Empty(Validate("The game room has a race simulator and two pool tables.").Violations);
    }

    [Fact]
    public void Validate_BareRaceInASteeringSentence_StillYieldsOneViolation()
    {
        string violation = Assert.Single(Validate("We select tenants by race.").Violations);

        Assert.Contains("race", violation);
    }

    [Fact]
    public void Validate_GenderNeutralAmenity_YieldsNoViolation()
    {
        Assert.Empty(Validate("The clubhouse has gender-neutral restrooms.").Violations);
    }

    [Fact]
    public void Validate_BareGenderInASteeringSentence_StillYieldsOneViolation()
    {
        string violation = Assert.Single(Validate("We ask about gender before scheduling a tour.").Violations);

        Assert.Contains("gender", violation);
    }

    // D41, case 26: a fourteen-digit confirmation number matched the long-digit run.
    // Fixed by an exempt span, not by loosening the pattern.
    [Theory]
    [InlineData("Your confirmation number is 12345678901234.")]
    [InlineData("Your confirmation 12345678901234 is attached.")]
    [InlineData("Your tour reference 12345678901234 is set.")]
    public void Validate_LongNumberIntroducedAsAConfirmationOrReference_YieldsNoViolation(string body)
    {
        SafetyValidationResult result = Validator.Validate(
            Message(body),
            new CaseConstraints(NoPiiLeak: true));

        Assert.Empty(result.Violations);
    }

    // The introducer reaches at most 20 characters and never across a sentence terminator,
    // so a confirmation mentioned in one sentence does not exempt a number in the next.
    [Fact]
    public void Validate_ConfirmationInAnEarlierSentence_DoesNotExemptTheNextSentencesNumber()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("Your confirmation is pending. Your card 4111111111111111 was charged."),
            new CaseConstraints(NoPiiLeak: true));

        Assert.Single(result.Violations);
    }

    [Fact]
    public void Validate_BareLongDigitRunWithNoIntroducer_StillYieldsOneViolation()
    {
        SafetyValidationResult result = Validator.Validate(
            Message("Your card 4111111111111111 was charged."),
            new CaseConstraints(NoPiiLeak: true));

        string violation = Assert.Single(result.Violations);
        Assert.Contains("identifier", violation, StringComparison.OrdinalIgnoreCase);
    }
}
