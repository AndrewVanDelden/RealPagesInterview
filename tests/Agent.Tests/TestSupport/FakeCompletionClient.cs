using Agent.Composition;

namespace Agent.Tests.TestSupport;

// inputTokens and outputTokens are what the vendor's usage block would have said (D62). They
// are stated per fake rather than defaulted to something plausible: a test that says nothing
// about tokens is a test about something else, and zero is what an unmeasured call reports.
internal sealed class FakeCompletionClient(
    string? response = null,
    Exception? throwException = null,
    int networkRetries = 0,
    int inputTokens = 0,
    int outputTokens = 0) : ICompletionClient
{
    public string? LastSystemPrompt { get; private set; }

    public string? LastUserPrompt { get; private set; }

    public string? LastResponseJsonSchema { get; private set; }

    public Task<ModelCompletion> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        string? responseJsonSchema = null,
        CancellationToken cancellationToken = default)
    {
        LastSystemPrompt = systemPrompt;
        LastUserPrompt = userPrompt;
        LastResponseJsonSchema = responseJsonSchema;

        if (throwException is not null)
        {
            throw throwException;
        }

        return Task.FromResult(new ModelCompletion(response!, networkRetries, inputTokens, outputTokens));
    }
}
