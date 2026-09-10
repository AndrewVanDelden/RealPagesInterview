using System.Net;
using System.Text;

namespace Agent.Tests.TestSupport;

// A transport whose answer the test computes per request, from the request body, so a test can
// fail one request with an exception rather than a status, or answer two concurrent calls on one
// client differently and in an order the test controls. FakeHttpMessageHandler scripts responses
// by call index, which cannot tell two concurrent calls apart.
internal sealed class CallbackHttpMessageHandler(Func<string, CancellationToken, Task<(HttpStatusCode Status, string Body)>> respond) : HttpMessageHandler
{
    private int callCount;

    public int CallCount => Volatile.Read(ref callCount);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref callCount);
        string requestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        (HttpStatusCode status, string body) = await respond(requestBody, cancellationToken);

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
