namespace Agent.Composition;

internal sealed record OpenAiChatResponse(IReadOnlyList<OpenAiChatChoice>? Choices);

internal sealed record OpenAiChatChoice(OpenAiChatMessage? Message);

internal sealed record OpenAiChatMessage(string? Content);
