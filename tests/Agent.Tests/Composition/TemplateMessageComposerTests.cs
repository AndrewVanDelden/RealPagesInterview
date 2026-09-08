using Agent.Common;
using Agent.Composition;
using Agent.Domain;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Composition;

public class TemplateMessageComposerTests
{
    private static readonly IMessageComposer Composer = new TemplateMessageComposer();

    [Fact]
    public async Task ComposeAsync_SmsWithCityInterest_BodyContainsRequiredElements()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: "Richardson, TX", amenityInterest: null);

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        NextMessage message = result.Value!.Message;
        Assert.Contains("Taylor", message.Body);
        Assert.Contains("Oak Ridge Apartments", message.Body);
        Assert.Contains("Richardson, TX", message.Body);
        Assert.Contains("book tour", message.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("STOP", message.Body);
        Assert.Equal("schedule_tour", message.Cta!.Type);
        Assert.Null(message.Subject);
        Assert.Null(message.SendAt);
    }

    [Fact]
    public async Task ComposeAsync_EmailWithAmenityInterest_BodyAndSubjectContainRequiredElements()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: null, amenityInterest: ["pool", "fitness"]);

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        Assert.True(result.IsSuccess);
        NextMessage message = result.Value!.Message;
        Assert.Contains("Taylor", message.Body);
        Assert.Contains("pool", message.Body);
        Assert.Contains("fitness", message.Body);
        Assert.Contains("book tour", message.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("STOP", message.Body);
        Assert.NotNull(message.Subject);
        Assert.Contains("Oak Ridge Apartments", message.Subject);
    }

    [Fact]
    public async Task ComposeAsync_AmenityAndCityInterestBothPresent_BodyMentionsBoth()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: "Richardson, TX", amenityInterest: ["pool"]);

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        NextMessage message = result.Value!.Message;
        Assert.Contains("pool", message.Body);
        Assert.Contains("Richardson, TX", message.Body);
    }

    [Fact]
    public async Task ComposeAsync_NoInterestProvided_OmitsInterestPhrase()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: null, amenityInterest: null);

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("interested in", result.Value!.Message.Body);
        Assert.DoesNotContain("looking in", result.Value.Message.Body);
    }

    [Fact]
    public async Task ComposeAsync_UnrecognizedPrimaryCta_PassesCtaTypeThroughUnchanged()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: "call_now");

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.Equal("call_now", result.Value!.Message.Cta!.Type);
    }

    // A12: an absent first name means no name in the greeting, not a failed record.
    [Fact]
    public async Task ComposeAsync_AbsentFirstName_ComposesWithoutAName()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(firstName: null);

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("Taylor", result.Value.Message.Body);
        Assert.StartsWith("Hi!", result.Value.Message.Body);
        Assert.Contains("STOP", result.Value.Message.Body);
    }

    [Fact]
    public async Task ComposeAsync_AbsentPropertyName_ComposesWithoutAPropertyFact()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(propertyName: null);

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("Oak Ridge", result.Value.Message.Body);
        Assert.Equal("Your next step", result.Value.Message.Subject);
    }

    // A9: no primary_cta means the generic reply call to action.
    [Fact]
    public async Task ComposeAsync_AbsentPrimaryCta_UsesTheGenericReplyCallToAction()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: null);

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.Equal("reply", result.Value.Message.Cta!.Type);
        Assert.Contains("Reply to learn more", result.Value.Message.Body);
    }

    [Fact]
    public async Task ComposeAsync_BlankPrimaryCta_IsTreatedAsAbsent()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: "  ");

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.Equal("reply", result.Value.Message.Cta!.Type);
    }

    [Fact]
    public async Task ComposeAsync_NoContextAtAll_StillComposesAMessageWithOptOut()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal() with { Input = null, Assertions = null };

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.Contains("STOP", result.Value.Message.Body);
        Assert.Equal("reply", result.Value.Message.Cta!.Type);
    }

    // D24 and playbook step 57: the result names the implementation that produced it, so a
    // fallback on an openai run shows up in the diagnostics instead of passing for a clean run.
    [Fact]
    public async Task ComposeAsync_AnyRecord_NamesItselfAsTheComposerOfOneAttempt()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.Equal(ComposerNames.Template, result.Value!.Notes.Composer);
        Assert.Equal(1, result.Value.Notes.Attempts);
    }

    // A10 and D25: sms carries the numbered reply options in the body and in cta.options,
    // the way sample 1's label does; the content comes from the catalog, since no input
    // field states it.
    [Fact]
    public async Task ComposeAsync_SmsWithKnownCta_CarriesNumberedOptionsInBodyAndCta()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        NextMessage message = result.Value!.Message;
        Assert.Equal(["Thu", "Fri"], message.Cta!.Options);
        Assert.Contains("Reply 1 for Thu, 2 for Fri.", message.Body);
        Assert.Null(message.Cta.Link);
    }

    // A10 and A21: email carries the link, built from the property slug and the catalog's
    // path for this call to action, and the body carries the same link.
    [Fact]
    public async Task ComposeAsync_EmailWithKnownCta_CarriesTheLinkBuiltFromThePropertySlug()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        NextMessage message = result.Value!.Message;
        Assert.Equal(new Uri("https://oakridge.example/tour"), message.Cta!.Link);
        Assert.Contains("https://oakridge.example/tour", message.Body);
        Assert.Null(message.Cta.Options);
    }

    // A21: no property name, no host, so no link is invented and the payload check fails
    // honestly on that record.
    [Fact]
    public async Task ComposeAsync_EmailWithoutPropertyName_CarriesNoLink()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(propertyName: null);

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        Assert.Null(result.Value!.Message.Cta!.Link);
    }

    // A21: the slug is the property name lowercased, with a trailing property-type word
    // dropped and every non-alphanumeric character removed.
    [Theory]
    [InlineData("Oak Ridge Apartments", "https://oakridge.example/tour")]
    [InlineData("Oak Ridge", "https://oakridge.example/tour")]
    [InlineData("St. James Lofts", "https://stjames.example/tour")]
    [InlineData("Lofts", "https://lofts.example/tour")]
    public async Task ComposeAsync_EmailWithAnyPropertyName_BuildsTheSlugFromIt(string propertyName, string expectedLink)
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(propertyName: propertyName);

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        Assert.Equal(new Uri(expectedLink), result.Value!.Message.Cta!.Link);
    }

    // A9 and D25: an unknown primary_cta passes through as the type, and the payload comes
    // from the generic row, since the shape is the channel's rule (A10) and only the content
    // is the catalog's.
    [Fact]
    public async Task ComposeAsync_UnknownPrimaryCtaOnSms_PassesTheTypeThroughWithTheGenericPayload()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: "call_now");

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Cta cta = result.Value!.Message.Cta!;
        Assert.Equal("call_now", cta.Type);
        Assert.Equal(["a question", "a tour"], cta.Options);
    }

    // A9: no primary_cta means the generic reply call to action, and its own payload.
    [Fact]
    public async Task ComposeAsync_AbsentPrimaryCtaOnEmail_UsesTheGenericLinkPath()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: null);

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        Assert.Equal(new Uri("https://oakridge.example/reply"), result.Value!.Message.Cta!.Link);
    }

    // A21: a property name with no letters or digits in it leaves no host, so the record
    // gets no link rather than "https://.example/tour".
    [Fact]
    public async Task ComposeAsync_EmailWithPunctuationOnlyPropertyName_CarriesNoLink()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(propertyName: "!!!");

        Result<ComposedMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        Assert.Null(result.Value!.Message.Cta!.Link);
    }
}
