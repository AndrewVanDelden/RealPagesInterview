using System.ClientModel;
using System.Net;
using Agent.Composition;
using Agent.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Agent.Tests.Composition;

// The client runs on the official OpenAI package, not hand-rolled HTTP, so these tests drive
// the real SDK pipeline over a fake transport. What they pin is this project's contract with
// it: the request the SDK builds from our options, the retries it spends and reports, and the
// failures the composer above has to catch.
public class OpenAiCompletionClientTests
{
    private const string CompletionJson = """
        {"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"gpt-4o-mini",
         "choices":[{"index":0,"message":{"role":"assistant","content":"{\"body\":\"hi\"}"},"finish_reason":"stop"}]}
        """;

    // The same completion with the vendor's usage block, which a real 200 always carries.
    // CompletionJson deliberately keeps none, so the two together cover both answers the SDK
    // can give for ChatCompletion.Usage.
    private const string UsageCompletionJson = """
        {"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"gpt-4o-mini",
         "choices":[{"index":0,"message":{"role":"assistant","content":"{\"body\":\"hi\"}"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":11,"completion_tokens":7,"total_tokens":18}}
        """;

    // A completed call whose one choice has an empty body, with the usage block the vendor
    // billed it by.
    private const string EmptyContentUsageJson = """
        {"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"gpt-4o-mini",
         "choices":[{"index":0,"message":{"role":"assistant","content":""},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":11,"completion_tokens":7,"total_tokens":18}}
        """;

    [Fact]
    public async Task CompleteAsync_SuccessfulResponse_ReturnsMessageContentAndNoRetries()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, CompletionJson));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));

        ModelCompletion completion = await client.CompleteAsync("system", "user");

        Assert.Equal("{\"body\":\"hi\"}", completion.Content);
        Assert.Equal(0, completion.NetworkRetries);
        Assert.Equal(1, handler.CallCount);
    }

    // The SDK raises ClientResultException for a non-success status; the composer catches it
    // and turns it into a Result failure rather than letting it escape the agent.
    [Fact]
    public async Task CompleteAsync_ErrorStatusCode_ThrowsClientResultException()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid key"}}"""));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));

        ClientResultException exception = await Assert.ThrowsAsync<ClientResultException>(() => client.CompleteAsync("system", "user"));

        Assert.Equal(401, exception.Status);
    }

    // Playbook step 49: a transient failure is retried once, and the count reaches
    // the diagnostics rather than disappearing inside the pipeline, since a retry nobody can see
    // is silent degradation.
    [Fact]
    public async Task CompleteAsync_TransientFailureThenSuccess_RetriesOnceAndReportsIt()
    {
        var handler = new FakeHttpMessageHandler(
            (HttpStatusCode.TooManyRequests, """{"error":{"message":"slow down"}}"""),
            (HttpStatusCode.OK, CompletionJson));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));

        ModelCompletion completion = await client.CompleteAsync("system", "user");

        Assert.Equal("{\"body\":\"hi\"}", completion.Content);
        Assert.Equal(1, completion.NetworkRetries);
        Assert.Equal(2, handler.CallCount);
    }

    // Retries are counted per call, which is what lets the batch run records concurrently: a
    // call's retries are the ones that call spent, not a difference read across a counter the
    // client shares. Two calls in flight on one client at once: the first meets a 429 and is
    // retried, and the second answers cleanly but only after the first's retry has been sent. A
    // before-and-after difference of one shared counter hands both calls attempts that were not
    // theirs.
    [Fact]
    public async Task CompleteAsync_TwoConcurrentCallsOneRetried_EachReportsOnlyItsOwnRetries()
    {
        var retriedCallsSecondAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int retriedCallRequests = 0;
        var handler = new CallbackHttpMessageHandler(async (requestBody, cancellationToken) =>
        {
            if (requestBody.Contains("retried call", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref retriedCallRequests) == 1)
                {
                    return (HttpStatusCode.TooManyRequests, """{"error":{"message":"slow down"}}""");
                }

                retriedCallsSecondAttempt.TrySetResult();
                return (HttpStatusCode.OK, CompletionJson);
            }

            await retriedCallsSecondAttempt.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return (HttpStatusCode.OK, CompletionJson);
        });
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));

        Task<ModelCompletion> retried = client.CompleteAsync("system", "retried call");
        Task<ModelCompletion> clean = client.CompleteAsync("system", "clean call");
        ModelCompletion[] completions = await Task.WhenAll(retried, clean);

        Assert.Equal(1, completions[0].NetworkRetries);
        Assert.Equal(0, completions[1].NetworkRetries);
        Assert.Equal(3, handler.CallCount);
    }

    // Bounded: one retry, then the failure is the caller's problem. A pipeline that kept
    // retrying would spend the record's whole latency budget on one call.
    [Fact]
    public async Task CompleteAsync_TransientFailureEveryTime_StopsAfterTheBoundedRetry()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.ServiceUnavailable, """{"error":{"message":"down"}}"""));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));

        await Assert.ThrowsAsync<ClientResultException>(() => client.CompleteAsync("system", "user"));

        Assert.Equal(2, handler.CallCount);
    }

    // The budget is a stated value, so a call that outlives it fails instead of holding
    // the batch open, and the client names the failure TimeoutException so the composer
    // catches a timeout by its own type. The timeout is not retried. It says the
    // completion did not fit the budget, and an identical second call inside the same budget
    // has no mechanism by which it would, while about a third of abandoned attempts are billed
    // in full: one HTTP attempt, never two.
    [Fact]
    public async Task CompleteAsync_CallOutlivesTheTimeout_ThrowsTimeoutExceptionWithoutRetrying()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, CompletionJson)) { Delay = TimeSpan.FromSeconds(5) };
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System), callBudget: TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<TimeoutException>(() => client.CompleteAsync("system", "user"));

        Assert.Equal(1, handler.CallCount);
    }

    // A timeout is not retried, so dividing the budget by the attempts the client may make would
    // only halve the one attempt that actually happens. A completion was measured at about 1.5 to
    // 4.5 s, so an attempt that needs 1200 ms of a 2000 ms budget is the case that matters: it
    // completes, where a divided budget abandoned it at 1000 ms.
    [Fact]
    public async Task CompleteAsync_AttemptNeedsMoreThanHalfTheBudget_StillCompletes()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, CompletionJson)) { Delay = TimeSpan.FromMilliseconds(1200) };
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System), callBudget: TimeSpan.FromMilliseconds(2000));

        ModelCompletion completion = await client.CompleteAsync("system", "user");

        Assert.Equal("{\"body\":\"hi\"}", completion.Content);
        Assert.Equal(1, handler.CallCount);
    }

    // The stated cost of giving one attempt the whole budget: the attempt after a transient
    // status is given the whole budget too, so a call that met a 429 can take the first attempt,
    // the backoff and a second full budget. The call completes rather than being cut at the
    // budget it has already exceeded.
    [Fact]
    public async Task CompleteAsync_TransientStatusThenASlowAttempt_GivesTheRetryTheWholeBudgetToo()
    {
        int requests = 0;
        var handler = new CallbackHttpMessageHandler(async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                return (HttpStatusCode.TooManyRequests, """{"error":{"message":"slow down"}}""");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken);
            return (HttpStatusCode.OK, CompletionJson);
        });
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System), callBudget: TimeSpan.FromMilliseconds(2000));

        ModelCompletion completion = await client.CompleteAsync("system", "user");

        Assert.Equal(1, completion.NetworkRetries);
        Assert.Equal(2, handler.CallCount);
    }

    // Only a transient status (408, 429, 5xx) is retried, so a request that fails with no
    // response at all is not retried either: the SDK would otherwise make it twice and hand the
    // composer an AggregateException of the two, a type its catch list does not name, which
    // escapes the agent and costs the record its output row. One attempt, and the SDK's own
    // ClientResultException, which the composer does catch.
    [Fact]
    public async Task CompleteAsync_RequestFailsWithNoResponse_IsNotRetried()
    {
        var handler = new CallbackHttpMessageHandler((_, _) => throw new HttpRequestException("connection reset"));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));

        await Assert.ThrowsAsync<ClientResultException>(() => client.CompleteAsync("system", "user"));

        Assert.Equal(1, handler.CallCount);
    }

    // A caller's cancellation arrives in the same shapes as a timeout and is not one: it
    // propagates untouched, so a cancelled batch stops instead of logging model failures.
    [Fact]
    public async Task CompleteAsync_CallerCancels_PropagatesTheCancellation()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, CompletionJson)) { Delay = TimeSpan.FromSeconds(5) };
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CompleteAsync("system", "user", null, cancellation.Token));
    }

    // A choice with an empty body is a completed call the vendor billed, so it is returned as a
    // completion with its usage counts rather than thrown: a throw after the usage block was read
    // lands in the composer's catch for calls that never completed, which counts it as abandoned
    // with zero tokens. The composer turns the empty content into a failed outcome.
    [Fact]
    public async Task CompleteAsync_ResponseHasEmptyContentButCarriesUsage_ReturnsAnEmptyCompletionWithTheVendorsTokenCounts()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, EmptyContentUsageJson));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));

        ModelCompletion completion = await client.CompleteAsync("system", "user");

        Assert.Equal(string.Empty, completion.Content);
        Assert.Equal(11, completion.InputTokens);
        Assert.Equal(7, completion.OutputTokens);
    }

    // Playbook step 52: a low temperature is set, and never called determinism.
    [Fact]
    public async Task CompleteAsync_AnyRequest_CarriesTheModelTheMessagesAndALowTemperature()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, CompletionJson));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));

        await client.CompleteAsync("system prompt", "user prompt");

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"model\"", handler.LastRequestBody);
        Assert.Contains("\"messages\"", handler.LastRequestBody);
        Assert.Contains("system prompt", handler.LastRequestBody);
        Assert.Contains("user prompt", handler.LastRequestBody);
        Assert.Contains("\"temperature\":0.2", handler.LastRequestBody);
    }

    // Playbook step 51: constrained decoding is enforced by the API, so the request has to
    // carry the schema in strict mode, not prose asking for it.
    [Fact]
    public async Task CompleteAsync_ResponseJsonSchemaProvided_RequestUsesStrictJsonSchemaMode()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, CompletionJson));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));
        const string schema = """{"type":"object","properties":{"body":{"type":"string"}},"required":["body"],"additionalProperties":false}""";

        await client.CompleteAsync("system", "user", schema);

        Assert.Contains("\"json_schema\"", handler.LastRequestBody);
        Assert.Contains("\"strict\":true", handler.LastRequestBody);
        Assert.Contains("\"additionalProperties\":false", handler.LastRequestBody);
    }

    // No schema means the weaker guarantee, valid JSON of any shape, which is what the
    // judge asks for and what a caller with nothing specific to enforce gets.
    [Fact]
    public async Task CompleteAsync_NoResponseJsonSchema_RequestUsesPlainJsonObjectMode()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, CompletionJson));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));

        await client.CompleteAsync("system", "user");

        Assert.Contains("\"type\":\"json_object\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"json_schema\"", handler.LastRequestBody);
    }

    // A 200 with no choice at all is a response shape the API can return and the composer
    // has to survive: it is an InvalidOperationException (NoCompletionChoiceException, a
    // subtype, so a Failed outcome carrying real tokens is still an InvalidOperationException
    // to a catch that has not been told about the subtype), which the composer catches into a
    // Result failure and the loop turns into a fallback. Anything else escapes the agent and
    // costs the record its output row.
    [Theory]
    [InlineData("""{"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"gpt-4o-mini","choices":[]}""")]
    [InlineData("""{"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"gpt-4o-mini"}""")]
    public async Task CompleteAsync_ResponseHasNoChoice_ThrowsInvalidOperationException(string responseJson)
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, responseJson));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));

        await Assert.ThrowsAsync<NoCompletionChoiceException>(() => client.CompleteAsync("system", "user"));
    }

    // A 200 with no choice can still carry a real usage block, because the vendor can
    // bill a call whose output was withheld (a moderation refusal is one live shape of this).
    // result.Value.Content is what throws when there is no choice, and result.Value.Usage is
    // already readable at that point, so the tokens must not collapse into the same zero an
    // abandoned-at-timeout call reports: no call, abandoned and completed are three facts.
    [Fact]
    public async Task CompleteAsync_ResponseHasNoChoiceButCarriesUsage_ThrowsWithTheVendorsTokenCounts()
    {
        const string responseJson = """
            {"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"gpt-4o-mini","choices":[],
             "usage":{"prompt_tokens":11,"completion_tokens":7,"total_tokens":18}}
            """;
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, responseJson));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));

        NoCompletionChoiceException exception = await Assert.ThrowsAsync<NoCompletionChoiceException>(() => client.CompleteAsync("system", "user"));

        Assert.Equal(11, exception.InputTokens);
        Assert.Equal(7, exception.OutputTokens);
    }

    // The other answer the SDK can give for Usage: no block at all, which is what
    // CompletionJson (used by the no-choice theory above) carries. Zero is the honest
    // reading of "nothing measured" here, the same rule a completed call with a choice but
    // no usage block already follows.
    [Fact]
    public async Task CompleteAsync_ResponseHasNoChoiceAndNoUsage_ThrowsWithZeroTokens()
    {
        const string responseJson = """{"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"gpt-4o-mini","choices":[]}""";
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, responseJson));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));

        NoCompletionChoiceException exception = await Assert.ThrowsAsync<NoCompletionChoiceException>(() => client.CompleteAsync("system", "user"));

        Assert.Equal(0, exception.InputTokens);
        Assert.Equal(0, exception.OutputTokens);
    }

    // A no-choice response that followed a transient retry made two attempts, and the retry is
    // as real as the tokens, so it rides on the exception with them.
    [Fact]
    public async Task CompleteAsync_ResponseHasNoChoiceAfterATransientRetry_ThrowsWithTheRetryCount()
    {
        const string noChoiceJson = """{"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"gpt-4o-mini","choices":[]}""";
        var handler = new FakeHttpMessageHandler((HttpStatusCode.ServiceUnavailable, "{}"), (HttpStatusCode.OK, noChoiceJson));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));

        NoCompletionChoiceException exception = await Assert.ThrowsAsync<NoCompletionChoiceException>(() => client.CompleteAsync("system", "user"));

        Assert.Equal(1, exception.NetworkRetries);
        Assert.Equal(2, handler.CallCount);
    }

    // Cost in the diagnostics is measured tokens, never money, and the measurement is the
    // vendor's own usage block on the completion. Confirmed by reflection over the restored OpenAI
    // 2.13.0 assembly on 2026-09-09: ChatCompletion.Usage is a ChatTokenUsage with int
    // InputTokenCount, int OutputTokenCount and int TotalTokenCount, and the wire names it
    // reads are prompt_tokens and completion_tokens.
    [Fact]
    public async Task CompleteAsync_ResponseCarriesUsage_ReportsTheVendorsTokenCounts()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, UsageCompletionJson));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));

        ModelCompletion completion = await client.CompleteAsync("system", "user");

        Assert.Equal(11, completion.InputTokens);
        Assert.Equal(7, completion.OutputTokens);
    }

    // A 200 whose body carries no usage block at all: the SDK leaves Usage null, so there is
    // nothing measured and the counts are zero. The call still completed, and it is
    // ModelCostNotes.CompletedCalls beside them, not the zeros, that says whether a bill
    // exists.
    [Fact]
    public async Task CompleteAsync_ResponseHasNoUsage_ReportsZeroTokens()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, CompletionJson));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(TimeProvider.System));

        ModelCompletion completion = await client.CompleteAsync("system", "user");

        Assert.Equal(0, completion.InputTokens);
        Assert.Equal(0, completion.OutputTokens);
    }

    // The limits a response reports reach the gate, so a call past them is not sent: with a limit
    // of one request a minute, the second call waits for the first to leave the window.
    [Fact]
    public async Task CompleteAsync_ResponseReportsItsLimits_TheNextCallPastThemWaitsForTheWindow()
    {
        var time = new FakeTimeProvider();
        var handler = new CallbackHttpMessageHandler(
            (_, _) => Task.FromResult((HttpStatusCode.OK, UsageCompletionJson)),
            LimitHeaders(requests: "1", tokens: "1000000"));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(time));

        await client.CompleteAsync("s", "u");
        Task<ModelCompletion> second = client.CompleteAsync("s", "u");

        // Long enough for a call the gate let through to reach the transport.
        await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.False(second.IsCompleted);
        Assert.Equal(1, handler.CallCount);
        time.Advance(TimeSpan.FromSeconds(60));
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, handler.CallCount);
    }

    // A failed call's response carries the limits too, and they are read from it: a 401 reporting
    // one request a minute holds the next call as a 200 would.
    [Fact]
    public async Task CompleteAsync_ErrorResponseReportsItsLimits_TheNextCallPastThemWaitsForTheWindow()
    {
        var time = new FakeTimeProvider();
        var handler = new CallbackHttpMessageHandler(
            (_, _) => Task.FromResult((HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid key"}}""")),
            LimitHeaders(requests: "1", tokens: "1000000"));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(time));

        await Assert.ThrowsAsync<ClientResultException>(() => client.CompleteAsync("s", "u"));
        Task<ModelCompletion> second = client.CompleteAsync("s", "u");

        // Long enough for a call the gate let through to reach the transport.
        await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.False(second.IsCompleted);
        Assert.Equal(1, handler.CallCount);
        time.Advance(TimeSpan.FromSeconds(60));
        await Assert.ThrowsAsync<ClientResultException>(() => second.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    // A limit header that is not a positive whole number, or a response that states only one of
    // the two limits, leaves the limits unknown: the next call goes out once the previous one is
    // over, without waiting for a window a misread limit would have imposed.
    [Theory]
    [InlineData("0", "1000000")]
    [InlineData("abc", "1000000")]
    [InlineData("1", null)]
    public async Task CompleteAsync_LimitHeadersNotUsable_TheNextCallGoesOutWhenThePreviousIsOver(string requests, string? tokens)
    {
        var handler = new CallbackHttpMessageHandler(
            (_, _) => Task.FromResult((HttpStatusCode.OK, UsageCompletionJson)),
            LimitHeaders(requests, tokens));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(new FakeTimeProvider()));

        await client.CompleteAsync("s", "u");
        await client.CompleteAsync("s", "u").WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, handler.CallCount);
    }

    // A call reserves its prompt estimate plus a 1,000-token reply allowance, and once it returns
    // counts what the vendor reported, never less than the prompt estimate. 2,070 of 2,300 tokens a
    // minute are usable; the first call reserves 1,001 and reports 11 in and 7 out, so it counts 18,
    // and two more calls of 1,001 then go out together. Counting the first at 1,001 would hold the
    // third, and the second's response is held until the third arrives.
    [Fact]
    public async Task CompleteAsync_CallReportsFewerTokensThanItReserved_GivesTheRestBackToLaterCalls()
    {
        var thirdArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        var handler = new CallbackHttpMessageHandler(
            async (_, cancellationToken) =>
            {
                int request = Interlocked.Increment(ref requests);
                if (request == 2)
                {
                    await thirdArrived.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                else if (request == 3)
                {
                    thirdArrived.TrySetResult();
                }

                return (HttpStatusCode.OK, UsageCompletionJson);
            },
            LimitHeaders(requests: "100", tokens: "2300"));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(new FakeTimeProvider()));

        await client.CompleteAsync("s", "u");
        await Task.WhenAll(client.CompleteAsync("s", "u"), client.CompleteAsync("s", "u")).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(3, handler.CallCount);
    }

    // A request the SDK retried counts against the limit like any other: with 2 of 3 requests a
    // minute usable, a call answered 503 then 200 has spent both, so the next call waits for the
    // window. Counting the call once would let it through.
    [Fact]
    public async Task CompleteAsync_CallWasRetried_TheRetryCountsAgainstTheLimit()
    {
        var time = new FakeTimeProvider();
        int requests = 0;
        var handler = new CallbackHttpMessageHandler(
            (_, _) => Task.FromResult(Interlocked.Increment(ref requests) == 1
                ? (HttpStatusCode.ServiceUnavailable, """{"error":{"message":"down"}}""")
                : (HttpStatusCode.OK, UsageCompletionJson)),
            LimitHeaders(requests: "3", tokens: "1000000"));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", new VendorRateLimitGate(time));

        ModelCompletion first = await client.CompleteAsync("s", "u");
        Task<ModelCompletion> second = client.CompleteAsync("s", "u");

        // Long enough for a call the gate let through to reach the transport.
        await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.Equal(1, first.NetworkRetries);
        Assert.False(second.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(60));
        await second.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // A call that times out is over as far as the gate is concerned, so the next call is not held
    // behind it.
    [Fact]
    public async Task CompleteAsync_CallTimesOut_TheNextCallStillGoesOut()
    {
        int requests = 0;
        var handler = new CallbackHttpMessageHandler(async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }

            return (HttpStatusCode.OK, UsageCompletionJson);
        });
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(
            httpClient, "fake-key", new VendorRateLimitGate(new FakeTimeProvider()), callBudget: TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<TimeoutException>(() => client.CompleteAsync("s", "u"));
        ModelCompletion second = await client.CompleteAsync("s", "u").WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(11, second.InputTokens);
    }

    private static Dictionary<string, string> LimitHeaders(string requests, string? tokens)
    {
        var headers = new Dictionary<string, string> { ["x-ratelimit-limit-requests"] = requests };
        if (tokens is not null)
        {
            headers["x-ratelimit-limit-tokens"] = tokens;
        }

        return headers;
    }
}
