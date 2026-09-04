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

    [Fact]
    public async Task ComposeAsync_CompletionClientThrowsHttpRequestException_ReturnsFailureNotException()
    {
        var composer = new OpenAiMessageComposer(new FakeCompletionClient(throwException: new HttpRequestException("503 Service Unavailable")));
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<NextMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.False(result.IsSuccess);
        Assert.Contains("503 Service Unavailable", result.Error);
    }

    [Fact]
    public async Task ComposeAsync_CompletionClientThrowsInvalidOperationException_ReturnsFailureNotException()
    {
        var composer = new OpenAiMessageComposer(new FakeCompletionClient(throwException: new InvalidOperationException("no completion content")));
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<NextMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.False(result.IsSuccess);
        Assert.Contains("no completion content", result.Error);
    }

    [Fact]
    public async Task ComposeAsync_UserPrompt_DelimitsIngestedDataFromInstructions()
    {
        const string json = """{"subject":null,"body":"hi","cta_type":"schedule_tour","cta_options":null,"cta_link":null}""";
        var fakeClient = new FakeCompletionClient(json);
        var composer = new OpenAiMessageComposer(fakeClient);
        ProspectCase prospectCase = SampleProspectCases.Minimal(firstName: "Taylor");

        await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.NotNull(fakeClient.LastUserPrompt);
        Assert.Contains("<prospect_data>", fakeClient.LastUserPrompt);
        Assert.Contains("</prospect_data>", fakeClient.LastUserPrompt);
        Assert.Contains("Taylor", fakeClient.LastUserPrompt);
    }

    [Fact]
    public async Task ComposeAsync_UserPrompt_InstructsRequiredCtaType()
    {
        const string json = """{"subject":null,"body":"hi","cta_type":"schedule_tour","cta_options":null,"cta_link":null}""";
        var fakeClient = new FakeCompletionClient(json);
        var composer = new OpenAiMessageComposer(fakeClient);
        ProspectCase prospectCase = SampleProspectCases.Minimal(primaryCta: "book_tour");

        await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Contains("schedule_tour", fakeClient.LastUserPrompt);
    }

    [Fact]
    public async Task ComposeAsync_UserPrompt_StatesOptOutRequiredWhenConstraintTrue()
    {
        const string json = """{"subject":null,"body":"hi","cta_type":"schedule_tour","cta_options":null,"cta_link":null}""";
        var fakeClient = new FakeCompletionClient(json);
        var composer = new OpenAiMessageComposer(fakeClient);
        ProspectCase prospectCase = SampleProspectCases.Minimal(includeOptOutInstructions: true);

        await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Contains("Opt-out instructions: required", fakeClient.LastUserPrompt);
    }

    [Fact]
    public async Task ComposeAsync_UserPrompt_StatesOptOutNotRequiredWhenConstraintFalse()
    {
        const string json = """{"subject":null,"body":"hi","cta_type":"schedule_tour","cta_options":null,"cta_link":null}""";
        var fakeClient = new FakeCompletionClient(json);
        var composer = new OpenAiMessageComposer(fakeClient);
        ProspectCase prospectCase = SampleProspectCases.Minimal(includeOptOutInstructions: false);

        await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Contains("Opt-out instructions: not required", fakeClient.LastUserPrompt);
    }

    [Fact]
    public async Task ComposeAsync_UserPrompt_MentionsBothAmenityAndCityInterestWhenBothPresent()
    {
        const string json = """{"subject":null,"body":"hi","cta_type":"schedule_tour","cta_options":null,"cta_link":null}""";
        var fakeClient = new FakeCompletionClient(json);
        var composer = new OpenAiMessageComposer(fakeClient);
        ProspectCase prospectCase = SampleProspectCases.Minimal(cityInterest: "Richardson, TX", amenityInterest: ["pool"]);

        await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Contains("pool", fakeClient.LastUserPrompt);
        Assert.Contains("Richardson, TX", fakeClient.LastUserPrompt);
    }

    [Fact]
    public async Task ComposeAsync_PriorViolationsProvided_UserPromptIncludesCorrectionFeedback()
    {
        const string json = """{"subject":null,"body":"hi","cta_type":"schedule_tour","cta_options":null,"cta_link":null}""";
        var fakeClient = new FakeCompletionClient(json);
        var composer = new OpenAiMessageComposer(fakeClient);
        ProspectCase prospectCase = SampleProspectCases.Minimal();
        string[] priorViolations = ["Missing required opt-out instructions."];

        await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms, priorViolations);

        Assert.Contains("Missing required opt-out instructions.", fakeClient.LastUserPrompt);
    }

    [Fact]
    public async Task ComposeAsync_NoPriorViolations_UserPromptHasNoCorrectionSection()
    {
        const string json = """{"subject":null,"body":"hi","cta_type":"schedule_tour","cta_options":null,"cta_link":null}""";
        var fakeClient = new FakeCompletionClient(json);
        var composer = new OpenAiMessageComposer(fakeClient);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.DoesNotContain("previous attempt", fakeClient.LastUserPrompt, StringComparison.OrdinalIgnoreCase);
    }
}
