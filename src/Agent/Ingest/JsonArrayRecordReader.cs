using System.Text.Json;
using Agent.Common;

namespace Agent.Ingest;

// Reads back what JsonArrayRecordWriter wrote: one indented JSON array of records. The file
// is one document, so the result is one Result for the whole file, unlike the JSONL reader's
// one Result per line (replay, D14).
public sealed class JsonArrayRecordReader<T>
{
    // O(n) in the file size.
    public Result<IReadOnlyList<T>> ReadAll(TextReader reader)
    {
        string json = reader.ReadToEnd();

        try
        {
            List<T>? records = JsonSerializer.Deserialize<List<T>>(json, AgentJsonOptions.Default);

            return records is null
                ? Result<IReadOnlyList<T>>.Failure("The file is not a JSON array of records (it is null).")
                : Result<IReadOnlyList<T>>.Success(records);
        }
        catch (JsonException ex)
        {
            return Result<IReadOnlyList<T>>.Failure($"The file is not a JSON array of records: {ex.Message}");
        }
    }
}
