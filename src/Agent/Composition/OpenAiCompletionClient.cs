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
// statuses it knows (408, 429, 500, 502, 503, 504), a per-call timeout the caller states, and
// a low temperature that is never called determinism (step 52). D33 narrows the retry to
// those statuses alone: a timeout, or any attempt that ends in an exception, is not retried
// (CountingRetryPolicy.ShouldRetryAsync).
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
    private static readonly TimeSpan DefaultCallBudget = TimeSpan.FromSeconds(30);

    private const string StructuredOutputSchemaName = "composed_message";

    private readonly ChatClient chatClient;
    private readonly CountingRetryPolicy retryPolicy;
    private readonly TimeSpan callBudget;

    // callBudget bounds one call including its retry, not one attempt: see PerAttemptTimeout.
    public OpenAiCompletionClient(HttpClient httpClient, string apiKey, string model = "gpt-4o-mini", TimeSpan? callBudget = null)
    {
        retryPolicy = new CountingRetryPolicy(MaxRetries);
        this.callBudget = callBudget ?? DefaultCallBudget;

        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(httpClient),
            NetworkTimeout = PerAttemptTimeout(this.callBudget),
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
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // D33: a timed-out attempt is never retried, so the pipeline rethrows that one
            // attempt's TaskCanceledException rather than an AggregateException of every
            // attempt's; a 429 retried before it adds no exception of its own. Named here so
            // the composer above catches a timeout by its own type instead of the SDK's, and
            // so a caller's cancellation, which arrives in the same shape, still propagates
            // untouched.
            throw new TimeoutException($"OpenAI call exceeded its budget of {callBudget}.", ex);
        }

        int retries = Math.Max(retryPolicy.Attempts - attemptsBefore - 1, 0);

        string content;
        try
        {
            content = result.Value.Content.Count > 0 ? result.Value.Content[0].Text : string.Empty;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // A 200 whose body carries no choice at all. ClientResult deserializes on the
            // first read of Value, not on the await, so the SDK reaches for a choice that is
            // not there and throws from inside itself, after the call has already returned.
            // Named here as the same failure an empty message body is, because the composer
            // catches that one into a Result and the compose-validate loop turns it into a
            // fallback; an unhandled exception escapes all of that and costs the record its
            // output row. This is the guarantee the hand-rolled client gave with
            // "Choices?.FirstOrDefault()?.Message?.Content ?? throw" before D27 replaced it.
            //
            // D62: unlike a timeout, result.Value is already read by this point (it is what
            // threw), so result.Value.Usage is real and readable - the vendor can still bill
            // a response with no choice. Read here and carried on the exception, because it
            // has nowhere else to travel once this throws.
            ChatTokenUsage? noChoiceUsage = result.Value.Usage;
            throw new NoCompletionChoiceException(noChoiceUsage?.InputTokenCount ?? 0, noChoiceUsage?.OutputTokenCount ?? 0, ex);
        }

        // D62: the measured cost of the call, read off the vendor's own usage block. Confirmed
        // against the restored assembly on 2026-09-09, not from documentation, which does not
        // list it (playbook step 50, SCS): ChatCompletion.Usage is an OpenAI.Chat.ChatTokenUsage
        // with int InputTokenCount, int OutputTokenCount and int TotalTokenCount, and it is null
        // when the response body carried no usage object. Read here, after the content, because
        // a call that never got this far is an abandoned call the client cannot measure at all:
        // the TimeoutException above is thrown before result.Value is ever read.
        ChatTokenUsage? usage = result.Value.Usage;

        return content.Length > 0
            ? new ModelCompletion(content, retries, usage?.InputTokenCount ?? 0, usage?.OutputTokenCount ?? 0)
            : throw new InvalidOperationException("OpenAI response contained no completion content.");
    }

    // NetworkTimeout bounds one attempt, and the policy above may make MaxRetries more of
    // them, so a budget handed straight to it would be exceeded by the retry beside it: a
    // bound that is documented and not enforced. Dividing makes the whole call, its retry
    // included, fit inside the budget the record stated. What this does not bound is the
    // compose-validate loop above: after a failed call it composes once more before falling
    // back, so a record that fails composition can spend up to twice its budget before the
    // template composer answers, which the p95 check then measures and reports.
    public static TimeSpan PerAttemptTimeout(TimeSpan callBudget) => callBudget / (1 + MaxRetries);

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
