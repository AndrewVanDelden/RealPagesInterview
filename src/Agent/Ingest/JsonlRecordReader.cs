using System.Text.Json;
using Agent.Common;
using Agent.Domain;

namespace Agent.Ingest;

public sealed class JsonlRecordReader
{
    // One result per non-blank line, in file order. A line that does not parse is a
    // failure row naming its 1-based line number; every other line still comes back
    // parsed (HB: failures are reported per record, never per batch).
    // O(n) time and O(n) space in the number of input lines: every result stays resident
    // until the caller takes the list.
    public IReadOnlyList<Result<ProspectCase>> ReadAll(TextReader reader)
    {
        var results = new List<Result<ProspectCase>>();
        int lineNumber = 0;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            results.Add(ReadLine(line, lineNumber));
        }

        return results;
    }

    private static Result<ProspectCase> ReadLine(string line, int lineNumber)
    {
        try
        {
            ProspectCase? prospectCase = JsonSerializer.Deserialize<ProspectCase>(line, AgentJsonOptions.Default);

            return prospectCase is null
                ? Result<ProspectCase>.Failure($"Line {lineNumber} deserialized to null.")
                : Result<ProspectCase>.Success(prospectCase);
        }
        catch (JsonException ex)
        {
            return Result<ProspectCase>.Failure($"Line {lineNumber} failed to parse: {ex.Message}");
        }
    }
}
