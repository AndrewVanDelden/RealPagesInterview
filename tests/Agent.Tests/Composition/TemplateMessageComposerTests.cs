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

        Result<NextMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        NextMessage message = result.Value!;
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

        Result<NextMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        Assert.True(result.IsSuccess);
        NextMessage message = result.Value!;
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

        Result<NextMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        NextMessage message = result.Value!;
        Assert.Contains("pool", message.Body);
        Assert.Contains("Richardson, TX", message.Body);
    }

    [Fact]
    public async Task ComposeAsync_NoInterestProvided_OmitsInterestPhrase()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: null, amenityInterest: null);

        Result<NextMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("interested in", result.Value!.Body);
        Assert.DoesNotContain("looking in", result.Value.Body);
    }

    [Fact]
    public async Task ComposeAsync_UnrecognizedPrimaryCta_PassesCtaTypeThroughUnchanged()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: "call_now");

        Result<NextMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.Equal("call_now", result.Value!.Cta!.Type);
    }

    // A12: an absent first name means no name in the greeting, not a failed record.
    [Fact]
    public async Task ComposeAsync_AbsentFirstName_ComposesWithoutAName()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(firstName: null);

        Result<NextMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("Taylor", result.Value.Body);
        Assert.StartsWith("Hi!", result.Value.Body);
        Assert.Contains("STOP", result.Value.Body);
    }

    [Fact]
    public async Task ComposeAsync_AbsentPropertyName_ComposesWithoutAPropertyFact()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(propertyName: null);

        Result<NextMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("Oak Ridge", result.Value.Body);
        Assert.Equal("Your next step", result.Value.Subject);
    }

    // A9: no primary_cta means the generic reply call to action.
    [Fact]
    public async Task ComposeAsync_AbsentPrimaryCta_UsesTheGenericReplyCallToAction()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: null);

        Result<NextMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.Equal("reply", result.Value.Cta!.Type);
        Assert.Contains("Reply to learn more", result.Value.Body);
    }

    [Fact]
    public async Task ComposeAsync_BlankPrimaryCta_IsTreatedAsAbsent()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: "  ");

        Result<NextMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.Equal("reply", result.Value.Cta!.Type);
    }

    [Fact]
    public async Task ComposeAsync_NoContextAtAll_StillComposesAMessageWithOptOut()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal() with { Input = null, Assertions = null };

        Result<NextMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.Contains("STOP", result.Value.Body);
        Assert.Equal("reply", result.Value.Cta!.Type);
    }
}
