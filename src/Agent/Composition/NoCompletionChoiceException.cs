namespace Agent.Composition;

// D62: a 200 with no completion choice can still carry a real usage block the vendor billed
// for (OpenAiCompletionClient.CompleteAsync reads result.Value to find the missing choice, and
// result.Value.Usage is already available there). The tokens have nowhere else to travel once
// the exception is thrown, so they ride on it rather than being read and discarded.
public sealed class NoCompletionChoiceException(int inputTokens, int outputTokens, Exception innerException)
    : InvalidOperationException("OpenAI response contained no completion choice.", innerException)
{
    public int InputTokens { get; } = inputTokens;

    public int OutputTokens { get; } = outputTokens;
}
