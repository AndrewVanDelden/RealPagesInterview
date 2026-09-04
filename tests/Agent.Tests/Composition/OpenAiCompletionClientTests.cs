using System.Net;
using System.Text;
using Agent.Composition;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Composition;

public class OpenAiCompletionClientTests
{
    [Fact]
    public async Task CompleteAsync_SuccessfulResponse_ReturnsMessageContent()
    {
        const string responseJson = """{"choices":[{"message":{"content":"{\"body\":\"hi\"}"}}]}""";
        using var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        };
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(httpResponse));
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");

        string result = await client.CompleteAsync("system", "user");

        Assert.Equal("{\"body\":\"hi\"}", result);
    }

    [Fact]
    public async Task CompleteAsync_ErrorStatusCode_ThrowsHttpRequestException()
    {
        using var httpResponse = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(httpResponse));
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync("system", "user"));
    }

    [Fact]
    public async Task CompleteAsync_ErrorStatusCodeWithBody_ExceptionMessageIncludesResponseBody()
    {
        const string errorJson = """{"error":{"message":"Rate limit reached","type":"rate_limit_error"}}""";
        using var httpResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(errorJson, Encoding.UTF8, "application/json"),
        };
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(httpResponse));
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync("system", "user"));

        Assert.Contains("Rate limit reached", exception.Message);
    }

    [Fact]
    public async Task CompleteAsync_RequestBody_UsesSnakeCaseFieldNames()
    {
        const string responseJson = """{"choices":[{"message":{"content":"ok"}}]}""";
        using var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        };
        var handler = new FakeHttpMessageHandler(httpResponse);
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");

        await client.CompleteAsync("system prompt", "user prompt");

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"response_format\"", handler.LastRequestBody);
        Assert.Contains("\"model\"", handler.LastRequestBody);
        Assert.Contains("\"messages\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task CompleteAsync_NoResponseJsonSchema_RequestUsesPlainJsonObjectMode()
    {
        const string responseJson = """{"choices":[{"message":{"content":"ok"}}]}""";
        using var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        };
        var handler = new FakeHttpMessageHandler(httpResponse);
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");

        await client.CompleteAsync("system", "user");

        Assert.Contains("\"type\":\"json_object\"", handler.LastRequestBody);
        Assert.Contains("\"json_schema\":null", handler.LastRequestBody);
    }

    [Fact]
    public async Task CompleteAsync_ResponseJsonSchemaProvided_RequestUsesStrictJsonSchemaMode()
    {
        const string responseJson = """{"choices":[{"message":{"content":"ok"}}]}""";
        using var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        };
        var handler = new FakeHttpMessageHandler(httpResponse);
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");
        const string schema = """{"type":"object","properties":{"body":{"type":"string"}},"required":["body"],"additionalProperties":false}""";

        await client.CompleteAsync("system", "user", schema);

        Assert.Contains("\"json_schema\"", handler.LastRequestBody);
        Assert.Contains("\"strict\":true", handler.LastRequestBody);
        Assert.Contains("\"additionalProperties\":false", handler.LastRequestBody);
    }

    [Fact]
    public async Task CompleteAsync_NoChoicesReturned_ThrowsInvalidOperationException()
    {
        const string responseJson = """{"choices":[]}""";
        using var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        };
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(httpResponse));
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync("system", "user"));
    }
}
