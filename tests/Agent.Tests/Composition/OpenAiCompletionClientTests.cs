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
