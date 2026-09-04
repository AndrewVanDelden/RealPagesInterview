using System.Text.Json;
using Agent.Common;
using Agent.Domain;

namespace Agent.Composition;

public sealed class OpenAiMessageComposer(ICompletionClient completionClient) : IMessageComposer
{
    private const string SystemPrompt =
        "You write short, compliant leasing messages for a residential property management company. " +
        "Always keep the brand voice warm and professional. Every message must include a clear call to " +
        "action and opt-out instructions. Never mention race, religion, national origin, familial status, " +
        "disability, or any other protected class, and never steer a prospect toward or away from a " +
        "neighborhood on that basis (fair housing). Never invent pricing or availability. " +
        "Respond with a single JSON object only, matching this shape: " +
        "{\"subject\": string or null, \"body\": string, \"cta_type\": string, " +
        "\"cta_options\": array of strings or null, \"cta_link\": url string or null}.";

    public async Task<Result<NextMessage>> ComposeAsync(ProspectCase prospectCase, CommunicationChannel channel, CancellationToken cancellationToken = default)
    {
        string userPrompt = BuildUserPrompt(prospectCase, channel);
        string rawResponse = await completionClient.CompleteAsync(SystemPrompt, userPrompt, cancellationToken);

        ComposedMessagePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ComposedMessagePayload>(rawResponse, AgentJsonOptions.Default);
        }
        catch (JsonException ex)
        {
            return Result<NextMessage>.Failure($"Model response was not valid JSON: {ex.Message}");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Body) || string.IsNullOrWhiteSpace(payload.CtaType))
        {
            return Result<NextMessage>.Failure("Model response was missing required fields (body, cta_type).");
        }

        var cta = new Cta(payload.CtaType, payload.CtaOptions, payload.CtaLink);
        var message = new NextMessage(channel, null, payload.Subject, payload.Body, cta);

        return Result<NextMessage>.Success(message);
    }

    private static string BuildUserPrompt(ProspectCase prospectCase, CommunicationChannel channel)
    {
        ProspectProfile profile = prospectCase.Input.Profile;
        string interest = profile.AmenityInterest is { Count: > 0 } amenities
            ? string.Join(", ", amenities)
            : profile.CityInterest ?? "no stated interest";

        return $"Channel: {channel}. " +
            $"Prospect first name: {profile.FirstName}. " +
            $"Property: {prospectCase.Input.PropertyName}. " +
            $"Stated interest: {interest}. " +
            $"Primary call to action: {prospectCase.Assertions.Constraints.PrimaryCta}.";
    }
}
