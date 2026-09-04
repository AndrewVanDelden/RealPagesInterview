using System.Text.Json;
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

            ProspectCase prospectCase;
            try
            {
                prospectCase = JsonSerializer.Deserialize<ProspectCase>(line, AgentJsonOptions.Default)
                    ?? throw new InvalidDataException($"Line {lineNumber} deserialized to null.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Line {lineNumber} failed to parse.", ex);
            }

            cases.Add(prospectCase);
        }

        return cases;
    }
}
