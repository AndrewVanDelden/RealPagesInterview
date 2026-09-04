using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Agent.Common;

namespace Agent.Composition;

public sealed class OpenAiCompletionClient(HttpClient httpClient, string apiKey, string model = "gpt-4o-mini") : ICompletionClient
{
    private const string CompletionsEndpoint = "https://api.openai.com/v1/chat/completions";

    private const string StructuredOutputSchemaName = "composed_message";

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        string? responseJsonSchema = null,
        CancellationToken cancellationToken = default)
    {
        var requestBody = new OpenAiChatRequest(
            model,
            [
                new OpenAiChatRequestMessage("system", systemPrompt),
                new OpenAiChatRequestMessage("user", userPrompt),
            ],
            BuildResponseFormat(responseJsonSchema));

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

    // "json_object" only guarantees syntactically valid JSON; it says nothing about shape.
    // When the caller supplies a schema, Structured Outputs (strict: true) makes the API
    // itself enforce that shape via constrained decoding, rather than trusting prose
    // instructions in the prompt to be honored.
    private static OpenAiResponseFormat BuildResponseFormat(string? responseJsonSchema)
    {
        if (responseJsonSchema is null)
        {
            return new OpenAiResponseFormat("json_object");
        }

        using JsonDocument schemaDocument = JsonDocument.Parse(responseJsonSchema);
        OpenAiJsonSchemaSpec schemaSpec = new(StructuredOutputSchemaName, Strict: true, schemaDocument.RootElement.Clone());

        return new OpenAiResponseFormat("json_schema", schemaSpec);
    }
}
