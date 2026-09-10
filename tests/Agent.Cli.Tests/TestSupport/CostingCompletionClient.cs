using Agent.Composition;

namespace Agent.Cli.Tests.TestSupport;

// D62: a completion client that answers with a canned completion and a stated token cost, so a
// CLI run can drive the model composer's cost path end to end without a network call or a key.
// Separate from FixedJudgeCompletionClient, which exists for --judge and counts the judge's
// calls; this one is the composer's client and states what the vendor would have billed.
internal sealed class CostingCompletionClient(string response, int inputTokens, int outputTokens) : ICompletionClient
{
    public Task<ModelCompletion> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        string? responseJsonSchema = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ModelCompletion(response, NetworkRetries: 0, inputTokens, outputTokens));
}
