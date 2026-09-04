using System.Text.Json;
using Agent.Common;
using Agent.Domain;

namespace Agent.Ingest;

public sealed class JsonlRecordReader : IRecordReader
{
    public IReadOnlyList<ProspectCase> ReadAll(string filePath)
    {
        var cases = new List<ProspectCase>();
        int lineNumber = 0;

        foreach (string line in File.ReadLines(filePath))
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            ProspectCase prospectCase;
            try
            {
                prospectCase = JsonSerializer.Deserialize<ProspectCase>(line, AgentJsonOptions.Default)
                    ?? throw new InvalidDataException($"Line {lineNumber} in '{filePath}' deserialized to null.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Line {lineNumber} in '{filePath}' failed to parse.", ex);
            }

            cases.Add(prospectCase);
        }

        return cases;
    }
}
