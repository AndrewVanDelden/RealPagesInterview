using Agent.Composition;

namespace Agent.Tests.TestSupport;

internal sealed class FakeCompletionClient(string? response = null, Exception? throwException = null) : ICompletionClient
{
    public string? LastSystemPrompt { get; private set; }

    public string? LastUserPrompt { get; private set; }

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        LastSystemPrompt = systemPrompt;
        LastUserPrompt = userPrompt;

        if (throwException is not null)
        {
            throw throwException;
        }

        return Task.FromResult(response!);
    }
}
