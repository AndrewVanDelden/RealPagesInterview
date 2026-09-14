namespace Agent.Composition;

// A 200 with no completion choice can still carry a real usage block the vendor billed
// for (OpenAiCompletionClient.CompleteAsync reads result.Value to find the missing choice, and
// result.Value.Usage is already available there), and it may have followed a transient retry.
// The tokens and the retry count have nowhere else to travel once the exception is thrown, so
// they ride on it rather than being read and discarded.
public sealed class NoCompletionChoiceException(int inputTokens, int outputTokens, int networkRetries, Exception innerException)
    : InvalidOperationException("OpenAI response contained no completion choice.", innerException)
{
    public int InputTokens { get; } = inputTokens;

    public int OutputTokens { get; } = outputTokens;

    public int NetworkRetries { get; } = networkRetries;
}
