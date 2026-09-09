using Agent.Composition;

namespace Agent.Cli.Tests.TestSupport;

// Fault-injection seam for --judge, the way ThrowingComposer is for the composer path:
// BuildJudge always reaches for a real OpenAiCompletionClient with no override of its own,
// so a test driving --judge through a non-empty batch supplies this instead of a real
// network call. Always returns the same canned grade, regardless of what was asked.
internal sealed class FixedJudgeCompletionClient(string response) : ICompletionClient
{
    public int CallCount { get; private set; }

    public Task<ModelCompletion> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        string? responseJsonSchema = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;

        return Task.FromResult(new ModelCompletion(response, NetworkRetries: 0));
    }
}
