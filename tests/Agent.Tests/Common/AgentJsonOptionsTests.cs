using System.Text.Json;
using Agent.Common;
using Xunit;

namespace Agent.Tests.Common;

public class AgentJsonOptionsTests
{
    private sealed record SampleText(string Value);

    // Output is a JSONL file a human is expected to read and hand off for review, not
    // markup embedded in a web page. The default encoder escapes ordinary punctuation
    // and any non-ASCII character to \uXXXX sequences (an apostrophe becomes ', an
    // accented letter becomes í) purely as an HTML/XSS precaution that does not
    // apply here, making every composed message unreadable in the raw file.
    [Fact]
    public void Default_SerializingApostrophe_DoesNotEscapeToUnicodeSequence()
    {
        string json = JsonSerializer.Serialize(new SampleText("you're welcome"), AgentJsonOptions.Default);

        Assert.Contains("you're welcome", json);
    }

    [Fact]
    public void Default_SerializingAccentedCharacter_DoesNotEscapeToUnicodeSequence()
    {
        string json = JsonSerializer.Serialize(new SampleText("Lucía"), AgentJsonOptions.Default);

        Assert.Contains("Lucía", json);
    }
}
