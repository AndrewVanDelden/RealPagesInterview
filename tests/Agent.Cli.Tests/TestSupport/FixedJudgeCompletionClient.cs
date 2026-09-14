using Agent.Composition;

namespace Agent.Cli.Tests.TestSupport;

// Fault-injection seam for --judge, the way ThrowingComposer is for the composer path:
// BuildJudge always reaches for a real OpenAiCompletionClient with no override of its own,
// so a test driving --judge through a non-empty batch supplies this instead of a real
// network call. Always returns the same canned grade, regardless of what was asked, with the
// token counts the vendor's usage block would have stated.
internal sealed class FixedJudgeCompletionClient(string response, int inputTokens = 0, int outputTokens = 0) : ICompletionClient
{
    public int CallCount { get; private set; }

    public Task<ModelCompletion> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        string? responseJsonSchema = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;

        return Task.FromResult(new ModelCompletion(response, NetworkRetries: 0, inputTokens, outputTokens));
    }
}
