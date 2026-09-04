using Agent.Common;
using Agent.Composition;
using Agent.Domain;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Composition;

public class OpenAiMessageComposerTests
{
    [Fact]
    public async Task ComposeAsync_ValidJsonResponse_ReturnsSuccessWithTypedMessage()
    {
        const string json = """{"subject":"Tour Oak Ridge","body":"Hi Taylor, book a tour!","cta_type":"book_tour","cta_options":["Thu","Fri"],"cta_link":null}""";
        var composer = new OpenAiMessageComposer(new FakeCompletionClient(json));
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<NextMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        NextMessage message = result.Value!;
        Assert.Equal("Hi Taylor, book a tour!", message.Body);
        Assert.Equal("Tour Oak Ridge", message.Subject);
        Assert.Equal("book_tour", message.Cta!.Type);
        Assert.Equal(["Thu", "Fri"], message.Cta.Options);
        Assert.Null(message.Cta.Link);
        Assert.Equal(CommunicationChannel.Sms, message.Channel);
    }

    [Fact]
    public async Task ComposeAsync_MalformedJson_ReturnsFailureNotException()
    {
        var composer = new OpenAiMessageComposer(new FakeCompletionClient("not json"));
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<NextMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ComposeAsync_MissingRequiredFields_ReturnsFailure()
    {
        const string json = """{"subject":"Tour","body":"","cta_type":""}""";
        var composer = new OpenAiMessageComposer(new FakeCompletionClient(json));
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<NextMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ComposeAsync_NullJsonBody_ReturnsFailure()
    {
        var composer = new OpenAiMessageComposer(new FakeCompletionClient("null"));
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<NextMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ComposeAsync_ProspectWithAmenityInterest_StillComposesSuccessfully()
    {
        const string json = """{"subject":null,"body":"hi","cta_type":"book_tour","cta_options":null,"cta_link":null}""";
        var composer = new OpenAiMessageComposer(new FakeCompletionClient(json));
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: null, amenityInterest: ["pool", "fitness"]);

        Result<NextMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Email);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ComposeAsync_ProspectWithNoStatedInterest_StillComposesSuccessfully()
    {
        const string json = """{"subject":null,"body":"hi","cta_type":"book_tour","cta_options":null,"cta_link":null}""";
        var composer = new OpenAiMessageComposer(new FakeCompletionClient(json));
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: null, amenityInterest: null);

        Result<NextMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Voice);

        Assert.True(result.IsSuccess);
    }
}
