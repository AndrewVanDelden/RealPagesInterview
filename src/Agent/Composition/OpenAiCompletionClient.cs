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

    // What one attempt may take unless the caller states otherwise. Sixty seconds, not a record's
    // p95_latency_ms: every call is paid for, and a call cut short spends the money and returns no
    // message. It stops a hung call; the p95 check still reports a run against the records' budget.
    public static readonly TimeSpan DefaultCallBudget = TimeSpan.FromSeconds(60);

    private const string StructuredOutputSchemaName = "composed_message";

    // What a call reserves for its reply before it is sent. Replies here run to about a hundred
    // tokens; the allowance is generous because a call that reserved too little could pass the
    // vendor's limit, and a completed call gives back what it did not use.
    private const int ReplyTokenAllowance = 1_000;

    private readonly ChatClient chatClient;
    private readonly CountingRetryPolicy retryPolicy;
    private readonly TimeSpan callBudget;
    private readonly VendorRateLimitGate rateLimitGate;

    // callBudget is the timeout of each attempt, whole, not a share of it. A timeout is never
    // retried, so the attempt that times out is the only one its call makes, and dividing the
    // budget across attempts would only halve it: one completion was measured at about 1.5 to
    // 4.5 s. The cost, stated rather than hidden: after a transient status the retry gets the
    // whole budget again, so such a call can take up to twice the budget plus the SDK's backoff.
    // Nor does this bound the compose-validate loop above: after a safety rejection it composes
    // once more before falling back, so a record can spend twice that again before the template
    // composer answers, which the p95 check measures and reports.
    // rateLimitGate is this model's gate: every call on this client waits in it, so one gate per
    // model keeps that model's calls under its limits (VendorRateLimitGate).
    public OpenAiCompletionClient(HttpClient httpClient, string apiKey, VendorRateLimitGate rateLimitGate, string model = "gpt-4o-mini", TimeSpan? callBudget = null)
    {
        this.rateLimitGate = rateLimitGate;
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

    // O(1) network calls, at most two: the call and its one retry. Before the call it waits in the
    // rate-limit gate for the tokens it estimates, and once it is over, whether it returned, failed or
    // timed out, it tells the gate what it counted for and the limits its response reported.
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

        // The vendor counts a request against its tokens per minute before it runs, from the
        // request's characters, so the estimate is taken from the same text: three characters a
        // token, more tokens than the usual four, plus the reply allowance.
        int promptTokenEstimate = (systemPrompt.Length + userPrompt.Length + (responseJsonSchema?.Length ?? 0) + 2) / 3;
        VendorCallReservation reservation = await rateLimitGate.ReserveAsync(promptTokenEstimate + ReplyTokenAllowance, cancellationToken);
        int countedTokens = promptTokenEstimate + ReplyTokenAllowance;
        VendorRateLimits? reportedLimits = null;
        try
        {
            ModelCompletion completion = await SendAsync(systemPrompt, userPrompt, options, cancellationToken, (limits, inputTokens, outputTokens) =>
            {
                reportedLimits = limits;
                countedTokens = Math.Max(promptTokenEstimate, inputTokens) + outputTokens;
            });
            return completion;
        }
        catch (ClientResultException ex)
        {
            // A request that failed with no response at all carries none, and so reports no limits.
            reportedLimits = ex.GetRawResponse() is { } response ? VendorRateLimits.FromHeaders(response.Headers) : null;
            throw;
        }
        finally
        {
            rateLimitGate.Complete(reservation, countedTokens, reportedLimits);
        }
    }

    // The call itself. onResponse receives the limits the response reported and the tokens it was
    // billed for as soon as they are known, including on the path that throws for a missing choice.
    private async Task<ModelCompletion> SendAsync(
        string systemPrompt,
        string userPrompt,
        ChatCompletionOptions options,
        CancellationToken cancellationToken,
        Action<VendorRateLimits?, int, int> onResponse)
    {
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

        // A call that returned was sent at least once, so this is never negative. Read before the
        // content, so a response with no choice carries the retries that came before it.
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
            // has returned. Named here so the composer turns it into a failed outcome and the
            // compose-validate loop into a fallback; an unhandled exception would cost the record
            // its output row. There is no content to return, unlike an empty message body, which
            // is returned below as a completion. Unlike a timeout,
            // result.Value has been read (it is what threw), so its Usage is real, and the vendor
            // can bill a response with no choice: the tokens ride on the exception, because they
            // have nowhere else to travel once this throws.
            ChatTokenUsage? noChoiceUsage = result.Value.Usage;
            onResponse(VendorRateLimits.FromHeaders(result.GetRawResponse().Headers), noChoiceUsage?.InputTokenCount ?? 0, noChoiceUsage?.OutputTokenCount ?? 0);
            throw new NoCompletionChoiceException(noChoiceUsage?.InputTokenCount ?? 0, noChoiceUsage?.OutputTokenCount ?? 0, retries, ex);
        }

        // The measured cost of the call, read off the vendor's own usage block. Confirmed
        // against the restored assembly on 2026-09-09, not from documentation, which does not
        // list it (playbook step 50, SCS): ChatCompletion.Usage is an OpenAI.Chat.ChatTokenUsage
        // with int InputTokenCount, int OutputTokenCount and int TotalTokenCount, and it is null
        // when the response body carried no usage object. Read here, after the content, because
        // a call that never got this far is an abandoned call the client cannot measure at all:
        // the TimeoutException above is thrown before result.Value is ever read.
        ChatTokenUsage? usage = result.Value.Usage;
        onResponse(VendorRateLimits.FromHeaders(result.GetRawResponse().Headers), usage?.InputTokenCount ?? 0, usage?.OutputTokenCount ?? 0);

        // An empty body is returned, not thrown. The call completed and its usage block is read
        // above, so it is a billed call, and the caller already has an exit for a completed call
        // whose content is unusable that keeps its counts: the composer's JSON parse fails on it
        // and reports the tokens and retries. A throw here would reach the composer's catch for
        // calls that never completed, which records them as abandoned with zero tokens.
        return new ModelCompletion(content, retries, usage?.InputTokenCount ?? 0, usage?.OutputTokenCount ?? 0);
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
