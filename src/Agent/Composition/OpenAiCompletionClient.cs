using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAI;
using OpenAI.Chat;

namespace Agent.Composition;

// D27: the real client goes through the official OpenAI package, pinned in
// Directory.Packages.props, rather than a hand-rolled call against the HTTP endpoint. Every
// type and parameter below was confirmed against that restored assembly, not against
// documentation (playbook step 50): ChatClient(model, ApiKeyCredential, OpenAIClientOptions),
// ChatClient.CompleteChatAsync(IEnumerable<ChatMessage>, ChatCompletionOptions,
// CancellationToken), ChatCompletionOptions.Temperature and .ResponseFormat,
// ChatResponseFormat.CreateJsonSchemaFormat(name, BinaryData, description, jsonSchemaIsStrict),
// OpenAIClientOptions.Transport, .NetworkTimeout and .RetryPolicy,
// HttpClientPipelineTransport(HttpClient) and ClientRetryPolicy(maxRetries).
//
// D28 bounds the call: one retry with the SDK's exponential backoff on the transient
// statuses it knows (408, 429, 5xx), a per-call timeout the caller states, and a low
// temperature that is never called determinism (step 52).
public sealed class OpenAiCompletionClient : ICompletionClient
{
    // One retry, not the SDK's default of three: a record states a latency budget, and a
    // pipeline that retries three times with backoff spends the whole of it on one call.
    private const int MaxRetries = 1;

    // Low, not zero: step 52. No provider promises reproducibility from temperature or a
    // seed, so variance is measured (Phase 7 step 83) rather than assumed away.
    private const float Temperature = 0.2f;

    // Only reached when no record in the batch states p95_latency_ms. It exists to stop a
    // hung call, not to express a budget.
    private static readonly TimeSpan DefaultCallTimeout = TimeSpan.FromSeconds(30);

    private const string StructuredOutputSchemaName = "composed_message";

    private readonly ChatClient chatClient;
    private readonly CountingRetryPolicy retryPolicy;
    private readonly TimeSpan callTimeout;

    public OpenAiCompletionClient(HttpClient httpClient, string apiKey, string model = "gpt-4o-mini", TimeSpan? callTimeout = null)
    {
        retryPolicy = new CountingRetryPolicy(MaxRetries);
        this.callTimeout = callTimeout ?? DefaultCallTimeout;

        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(httpClient),
            NetworkTimeout = this.callTimeout,
            RetryPolicy = retryPolicy,
        };

        chatClient = new ChatClient(model, new ApiKeyCredential(apiKey), options);
    }

    // O(1) network calls, at most two: the call and its one retry.
    public async Task<ModelCompletion> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        string? responseJsonSchema = null,
        CancellationToken cancellationToken = default)
    {
        var options = new ChatCompletionOptions
        {
            Temperature = Temperature,
            ResponseFormat = BuildResponseFormat(responseJsonSchema),
        };

        int attemptsBefore = retryPolicy.Attempts;

        ClientResult<ChatCompletion> result;
        try
        {
            result = await chatClient.CompleteChatAsync(
                [new SystemChatMessage(systemPrompt), new UserChatMessage(userPrompt)],
                options,
                cancellationToken);
        }
        catch (Exception ex) when (IsTimeout(ex) && !cancellationToken.IsCancellationRequested)
        {
            // The pipeline treats a timeout as transient and retries it, so an exhausted
            // call arrives as an AggregateException of TaskCanceledException rather than as
            // one cancellation. Named here so the composer above catches a timeout by its
            // own type instead of unwrapping the SDK's, and so a caller's cancellation,
            // which arrives in the same shapes, still propagates untouched.
            throw new TimeoutException($"OpenAI call exceeded the configured timeout of {callTimeout}.", ex);
        }

        int retries = Math.Max(retryPolicy.Attempts - attemptsBefore - 1, 0);

        string content = result.Value.Content.Count > 0 ? result.Value.Content[0].Text : string.Empty;

        return content.Length > 0
            ? new ModelCompletion(content, retries)
            : throw new InvalidOperationException("OpenAI response contained no completion content.");
    }

    private static bool IsTimeout(Exception exception) =>
        exception is OperationCanceledException ||
        (exception is AggregateException aggregate && aggregate.Flatten().InnerExceptions.Any(inner => inner is OperationCanceledException));

    // "json_object" only guarantees syntactically valid JSON; it says nothing about shape.
    // When the caller supplies a schema, Structured Outputs (strict) makes the API itself
    // enforce that shape via constrained decoding, rather than trusting prose instructions
    // in the prompt to be honored.
    private static ChatResponseFormat BuildResponseFormat(string? responseJsonSchema) =>
        responseJsonSchema is null
            ? ChatResponseFormat.CreateJsonObjectFormat()
            : ChatResponseFormat.CreateJsonSchemaFormat(
                StructuredOutputSchemaName,
                BinaryData.FromString(responseJsonSchema),
                jsonSchemaIsStrict: true);
}
