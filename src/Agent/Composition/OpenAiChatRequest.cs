namespace Agent.Composition;

internal sealed record OpenAiChatRequest(string Model, IReadOnlyList<OpenAiChatRequestMessage> Messages, OpenAiResponseFormat ResponseFormat);

internal sealed record OpenAiChatRequestMessage(string Role, string Content);

internal sealed record OpenAiResponseFormat(string Type);
