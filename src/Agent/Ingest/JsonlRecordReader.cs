using System.Text.Json;
using System.Text.Json.Nodes;
using Agent.Common;
using Agent.Domain;

namespace Agent.Ingest;

public sealed class JsonlRecordReader : IRecordReader
{
    public IReadOnlyList<ProspectCase> ReadAll(TextReader reader)
    {
        var cases = new List<ProspectCase>();
        int lineNumber = 0;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            cases.Add(ParseLine(line, lineNumber));
        }

        return cases;
    }

    // "expected" is the scoring oracle, not something the agent reads to decide anything
    // (DESIGN.md section 2) - its shape on a real hold-out file is not under our control.
    // It is parsed separately and leniently (falls back to null on any parse failure) so
    // one record's unrecognized "expected" shape cannot take down fields the agent
    // actually depends on, or the rest of the file.
    private static ProspectCase ParseLine(string line, int lineNumber)
    {
        JsonNode rootNode;
        try
        {
            rootNode = JsonNode.Parse(line) ?? throw new InvalidDataException($"Line {lineNumber} deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Line {lineNumber} failed to parse.", ex);
        }

        JsonNode? expectedNode = rootNode["expected"];
        rootNode["expected"] = null;

        // rootNode is already confirmed non-null above, and a non-null JsonNode
        // cannot deserialize to a null ProspectCase - the null-forgiving operator
        // reflects that structural guarantee, not an assumption.
        ProspectCase prospectCase;
        try
        {
            prospectCase = rootNode.Deserialize<ProspectCase>(AgentJsonOptions.Default)!;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Line {lineNumber} failed to parse.", ex);
        }

        return prospectCase with { Expected = TryParseExpected(expectedNode) };
    }

    private static ExpectedOutcome? TryParseExpected(JsonNode? expectedNode)
    {
        if (expectedNode is null)
        {
            return null;
        }

        try
        {
            return expectedNode.Deserialize<ExpectedOutcome>(AgentJsonOptions.Default);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
