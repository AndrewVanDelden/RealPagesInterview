using System.ClientModel.Primitives;
using System.Globalization;

namespace Agent.Composition;

// The per-minute limits the vendor reports for this key and model on every response, in the
// x-ratelimit-limit-requests and x-ratelimit-limit-tokens headers (OpenAI's rate-limit guide). They
// depend on the organization's usage tier, so they are read from the responses, never assumed.
public sealed record VendorRateLimits(int RequestsPerMinute, int TokensPerMinute)
{
    private const string RequestsHeader = "x-ratelimit-limit-requests";
    private const string TokensHeader = "x-ratelimit-limit-tokens";

    // Null when either header is absent or is not a positive whole number: a response that does not
    // state both limits leaves them unknown rather than half known.
    public static VendorRateLimits? FromHeaders(PipelineResponseHeaders headers)
    {
        int? requests = ReadPositive(headers, RequestsHeader);
        int? tokens = ReadPositive(headers, TokensHeader);
        return requests is { } requestsPerMinute && tokens is { } tokensPerMinute
            ? new VendorRateLimits(requestsPerMinute, tokensPerMinute)
            : null;
    }

    private static int? ReadPositive(PipelineResponseHeaders headers, string name) =>
        headers.TryGetValue(name, out string? value)
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            && parsed > 0
            ? parsed
            : null;
}
