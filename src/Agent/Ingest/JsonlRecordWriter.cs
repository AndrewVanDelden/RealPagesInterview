using System.Text.Json;
using Agent.Common;

namespace Agent.Ingest;

public sealed class JsonlRecordWriter<T> : IRecordWriter<T>
{
    public void WriteAll(TextWriter writer, IEnumerable<T> records)
    {
        foreach (T record in records)
        {
            writer.WriteLine(JsonSerializer.Serialize(record, AgentJsonOptions.Default));
        }
    }
}
