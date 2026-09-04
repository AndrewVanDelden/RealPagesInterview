using System.Text.Json;
using System.Text.Json.Serialization;
using Agent.Domain;

namespace Agent.Ingest;

public sealed class JsonlRecordReader : IRecordReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter<CommunicationChannel>(JsonNamingPolicy.CamelCase) },
    };

    public IReadOnlyList<ProspectCase> ReadAll(string filePath)
    {
        var cases = new List<ProspectCase>();

        foreach (string line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            ProspectCase prospectCase = JsonSerializer.Deserialize<ProspectCase>(line, SerializerOptions)
                ?? throw new InvalidDataException($"Line deserialized to null in '{filePath}'.");

            cases.Add(prospectCase);
        }

        return cases;
    }
}
