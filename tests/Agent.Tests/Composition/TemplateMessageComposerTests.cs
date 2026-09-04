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
        Assert.Equal("book_tour", message.Cta!.Type);
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
    public async Task ComposeAsync_NoInterestProvided_OmitsInterestPhrase()
    {
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: null, amenityInterest: null);

        Result<NextMessage> result = await Composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.DoesNotContain("interested in", result.Value!.Body);
        Assert.DoesNotContain("looking in", result.Value.Body);
    }
}
