using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using OpenAI;
using OpenAI.Chat;

namespace Agent.Composition;

// The real client goes through the official OpenAI package, pinned in Directory.Packages.props,
// rather than a hand-rolled HTTP call: the SDK owns the request shapes, the retry policy and the
// structured-output plumbing, and ICompletionClient is already the portability seam. Every type
// and parameter below was confirmed against that restored assembly, not against documentation
// (playbook step 50). The call is bounded: one retry with the SDK's backoff, only on the
// transient statuses 408, 429, 500, 502, 503 and 504, never on a timeout or other exception
// (CountingRetryPolicy.ShouldRetryAsync); a per-call timeout the caller states; and a low
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
    private static readonly TimeSpan DefaultCallBudget = TimeSpan.FromSeconds(30);

    private const string StructuredOutputSchemaName = "composed_message";

    private readonly ChatClient chatClient;
    private readonly CountingRetryPolicy retryPolicy;
    private readonly TimeSpan callBudget;

    // callBudget is the timeout of each attempt, whole, not a share of it. A timeout is never
    // retried, so the attempt that times out is the only one its call makes, and dividing the
    // budget across attempts would only halve it: one completion was measured at about 1.5 to
    // 4.5 s. The cost, stated rather than hidden: after a transient status the retry gets the
    // whole budget again, so such a call can take up to twice the budget plus the SDK's backoff.
    // Nor does this bound the compose-validate loop above: after a safety rejection it composes
    // once more before falling back, so a record can spend twice that again before the template
    // composer answers, which the p95 check measures and reports.
    public OpenAiCompletionClient(HttpClient httpClient, string apiKey, string model = "gpt-4o-mini", TimeSpan? callBudget = null)
    {
        retryPolicy = new CountingRetryPolicy(MaxRetries);
        this.callBudget = callBudget ?? DefaultCallBudget;

        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(httpClient),
            NetworkTimeout = this.callBudget,
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

        // This call's own attempt count, which concurrent calls on this client cannot add to
        // (CountingRetryPolicy.BeginCall).
        StrongBox<int> callAttempts = retryPolicy.BeginCall();

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
            // A timed-out attempt is never retried, so the pipeline rethrows that one
            // attempt's TaskCanceledException rather than an AggregateException of every
            // attempt's; a 429 retried before it adds no exception of its own. Named here so
            // the composer above catches a timeout by its own type instead of the SDK's, and
            // so a caller's cancellation, which arrives in the same shape, still propagates
            // untouched.
            throw new TimeoutException($"OpenAI call exceeded its budget of {callBudget}.", ex);
        }

        // A call that returned was sent at least once, so this is never negative.
        int retries = callAttempts.Value - 1;

        string content;
        try
        {
            content = result.Value.Content.Count > 0 ? result.Value.Content[0].Text : string.Empty;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // A 200 whose body carries no choice at all. ClientResult deserializes on the first
            // read of Value, not on the await, so the SDK throws from inside itself after the call
            // has returned. Named here as the same failure an empty message body is, which the
            // composer turns into a failed outcome and the compose-validate loop into a fallback;
            // an unhandled exception would cost the record its output row. Unlike a timeout,
            // result.Value has been read (it is what threw), so its Usage is real, and the vendor
            // can bill a response with no choice: the tokens ride on the exception, because they
            // have nowhere else to travel once this throws.
            ChatTokenUsage? noChoiceUsage = result.Value.Usage;
            throw new NoCompletionChoiceException(noChoiceUsage?.InputTokenCount ?? 0, noChoiceUsage?.OutputTokenCount ?? 0, ex);
        }

        // The measured cost of the call, read off the vendor's own usage block. Confirmed
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
