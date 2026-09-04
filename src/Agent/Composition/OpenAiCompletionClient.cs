using System.Net.Http.Headers;
using System.Net.Http.Json;
using Agent.Common;

namespace Agent.Composition;

public sealed class OpenAiCompletionClient(HttpClient httpClient, string apiKey, string model = "gpt-4o-mini") : ICompletionClient
{
    private const string CompletionsEndpoint = "https://api.openai.com/v1/chat/completions";

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var requestBody = new OpenAiChatRequest(
            model,
            [
                new OpenAiChatRequestMessage("system", systemPrompt),
                new OpenAiChatRequestMessage("user", userPrompt),
            ],
            new OpenAiResponseFormat("json_object"));

        using var request = new HttpRequestMessage(HttpMethod.Post, CompletionsEndpoint)
        {
            Content = JsonContent.Create(requestBody, options: AgentJsonOptions.Default),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"OpenAI request failed with status {(int)response.StatusCode} ({response.StatusCode}): {errorBody}",
                inner: null,
                response.StatusCode);
        }

        OpenAiChatResponse? chatResponse = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(AgentJsonOptions.Default, cancellationToken);

        return chatResponse?.Choices?.FirstOrDefault()?.Message?.Content
            ?? throw new InvalidOperationException("OpenAI response contained no completion content.");
    }
}
