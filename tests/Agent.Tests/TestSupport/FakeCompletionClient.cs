using Agent.Composition;

namespace Agent.Tests.TestSupport;

internal sealed class FakeCompletionClient(string response) : ICompletionClient
{
    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        => Task.FromResult(response);
}
