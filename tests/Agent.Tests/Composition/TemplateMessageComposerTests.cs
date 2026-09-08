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
}
