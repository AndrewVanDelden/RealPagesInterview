using System.Net.Http.Headers;
using System.Net.Http.Json;
using Agent.Common;

namespace Agent.Composition;

public sealed class OpenAiCompletionClient(HttpClient httpClient, string apiKey, string model = "gpt-4o-mini") : ICompletionClient
{
    private const string CompletionsEndpoint = "https://api.openai.com/v1/chat/completions";

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var requestBody = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
            response_format = new { type = "json_object" },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, CompletionsEndpoint)
        {
            Content = JsonContent.Create(requestBody),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        OpenAiChatResponse? chatResponse = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(AgentJsonOptions.Default, cancellationToken);

        return chatResponse?.Choices?.FirstOrDefault()?.Message?.Content
            ?? throw new InvalidOperationException("OpenAI response contained no completion content.");
    }
}
