using System.Net;
using System.Text;

namespace Agent.Tests.TestSupport;

// One scripted HTTP response per call, the last one repeating, so a test can drive the
// SDK's own retry policy (a 429 followed by a 200) as well as the single-response cases.
// A fresh HttpResponseMessage is built per call: a retried request cannot be handed the
// same already-read content twice.
internal sealed class FakeHttpMessageHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
{
    private readonly (HttpStatusCode Status, string Body)[] scripted =
        responses.Length > 0 ? responses : [(HttpStatusCode.OK, string.Empty)];

    public string? LastRequestBody { get; private set; }

    public int CallCount { get; private set; }

    public TimeSpan Delay { get; init; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        (HttpStatusCode status, string body) = scripted[Math.Min(CallCount, scripted.Length - 1)];
        CallCount++;

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
