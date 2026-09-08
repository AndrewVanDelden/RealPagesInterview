using System.ClientModel;
using System.Net;
using Agent.Composition;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Composition;

// D27: the client runs on the official OpenAI package, so these tests drive the real SDK
// pipeline over a fake transport. What they pin is this project's contract with it: the
// request the SDK builds from our options, the retries it spends and reports (D28), and the
// failures the composer above has to catch.
public class OpenAiCompletionClientTests
{
    private const string CompletionJson = """
        {"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"gpt-4o-mini",
         "choices":[{"index":0,"message":{"role":"assistant","content":"{\"body\":\"hi\"}"},"finish_reason":"stop"}]}
        """;

    private const string EmptyContentJson = """
        {"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"gpt-4o-mini",
         "choices":[{"index":0,"message":{"role":"assistant","content":""},"finish_reason":"stop"}]}
        """;

    [Fact]
    public async Task CompleteAsync_SuccessfulResponse_ReturnsMessageContentAndNoRetries()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, CompletionJson));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");

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
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");

        ClientResultException exception = await Assert.ThrowsAsync<ClientResultException>(() => client.CompleteAsync("system", "user"));

        Assert.Equal(401, exception.Status);
    }

    // D28 and playbook step 49: a transient failure is retried once, and the count reaches
    // the diagnostics rather than disappearing inside the pipeline.
    [Fact]
    public async Task CompleteAsync_TransientFailureThenSuccess_RetriesOnceAndReportsIt()
    {
        var handler = new FakeHttpMessageHandler(
            (HttpStatusCode.TooManyRequests, """{"error":{"message":"slow down"}}"""),
            (HttpStatusCode.OK, CompletionJson));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");

        ModelCompletion completion = await client.CompleteAsync("system", "user");

        Assert.Equal("{\"body\":\"hi\"}", completion.Content);
        Assert.Equal(1, completion.NetworkRetries);
        Assert.Equal(2, handler.CallCount);
    }

    // Bounded: one retry, then the failure is the caller's problem. A pipeline that kept
    // retrying would spend the record's whole latency budget on one call.
    [Fact]
    public async Task CompleteAsync_TransientFailureEveryTime_StopsAfterTheBoundedRetry()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.ServiceUnavailable, """{"error":{"message":"down"}}"""));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");

        await Assert.ThrowsAsync<ClientResultException>(() => client.CompleteAsync("system", "user"));

        Assert.Equal(2, handler.CallCount);
    }

    // D28: the per-call timeout is a stated value, so a call that outlives it fails instead
    // of holding the batch open. The pipeline retries a timeout, so the failure arrives as
    // an AggregateException of TaskCanceledException; the client names it TimeoutException
    // so the composer catches a timeout by its own type.
    [Fact]
    public async Task CompleteAsync_CallOutlivesTheTimeout_ThrowsTimeoutException()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, CompletionJson)) { Delay = TimeSpan.FromSeconds(5) };
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key", callTimeout: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<TimeoutException>(() => client.CompleteAsync("system", "user"));
    }

    // A caller's cancellation arrives in the same shapes as a timeout and is not one: it
    // propagates untouched, so a cancelled batch stops instead of logging model failures.
    [Fact]
    public async Task CompleteAsync_CallerCancels_PropagatesTheCancellation()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, CompletionJson)) { Delay = TimeSpan.FromSeconds(5) };
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CompleteAsync("system", "user", null, cancellation.Token));
    }

    [Fact]
    public async Task CompleteAsync_ResponseHasNoContent_ThrowsInvalidOperationException()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, EmptyContentJson));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync("system", "user"));
    }

    // Playbook step 52: a low temperature is set, and never called determinism.
    [Fact]
    public async Task CompleteAsync_AnyRequest_CarriesTheModelTheMessagesAndALowTemperature()
    {
        var handler = new FakeHttpMessageHandler((HttpStatusCode.OK, CompletionJson));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");

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
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");
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
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");

        await client.CompleteAsync("system", "user");

        Assert.Contains("\"type\":\"json_object\"", handler.LastRequestBody);
        Assert.DoesNotContain("\"json_schema\"", handler.LastRequestBody);
    }
}
