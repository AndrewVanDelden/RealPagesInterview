namespace Agent.Composition;

public interface ICompletionClient
{
    // responseJsonSchema, when provided, is a raw JSON Schema (draft-07 style) describing
    // the object the caller needs back. An implementation that supports constrained
    // decoding (OpenAI's Structured Outputs) should use it to enforce shape at the API
    // level rather than relying on prose instructions alone. Null means "any valid JSON."
    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        string? responseJsonSchema = null,
        CancellationToken cancellationToken = default);
}
