namespace Agent.Composition;

public interface ICompletionClient
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}
