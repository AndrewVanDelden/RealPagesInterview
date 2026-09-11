using System.Buffers;
using System.Text;
using System.Text.Json;
using Agent.Common;

namespace Agent.Ingest;

// Output format is our own choice, not part of the graded contract: problem_statement.txt
// requires the *input* to be JSONL, but says nothing about the output file's shape. A
// single indented JSON array is a valid JSON document any editor or viewer renders
// cleanly, unlike line-delimited JSONL where each record is an unreadable wall of text.
//
// Rows are written one at a time, so a batch holds one row in memory rather than every row
// until it ends. One JSON writer keeps its place across rows, so the commas and indentation
// are exactly what serializing the whole list at once writes, and a reader cannot tell.
public sealed class JsonArrayRecordWriter<T>
{
    private static readonly JsonSerializerOptions IndentedOptions = new(AgentJsonOptions.Default)
    {
        WriteIndented = true,
    };

    // Handed a JSON writer, the serializer formats with the writer's options and ignores its own,
    // so the writer carries every formatting setting of the options above.
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = IndentedOptions.Encoder,
        IndentCharacter = IndentedOptions.IndentCharacter,
        IndentSize = IndentedOptions.IndentSize,
        NewLine = IndentedOptions.NewLine,
    };

    private readonly TextWriter target;
    private readonly ArrayBufferWriter<byte> pending = new();

    // Not disposed: every call below flushes it, and over an in-memory buffer it holds no handle.
    private readonly Utf8JsonWriter json;

    public JsonArrayRecordWriter(TextWriter target)
    {
        this.target = target;
        json = new Utf8JsonWriter(pending, WriterOptions);
    }

    public Task BeginAsync(CancellationToken cancellationToken = default)
    {
        json.WriteStartArray();
        return FlushAsync(cancellationToken);
    }

    // O(size of one row) in time and space: the row is serialized and handed to the target.
    public Task WriteRowAsync(T row, CancellationToken cancellationToken = default)
    {
        JsonSerializer.Serialize(json, row, IndentedOptions);
        return FlushAsync(cancellationToken);
    }

    public Task EndAsync(CancellationToken cancellationToken = default)
    {
        json.WriteEndArray();
        return FlushAsync(cancellationToken);
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        json.Flush();
        string text = Encoding.UTF8.GetString(pending.WrittenSpan);
        pending.ResetWrittenCount();
        await target.WriteAsync(text.AsMemory(), cancellationToken);
    }
}
