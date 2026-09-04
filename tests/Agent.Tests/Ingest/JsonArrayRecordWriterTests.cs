using System.Text.Json;
using Agent.Domain;
using Agent.Ingest;
using Xunit;

namespace Agent.Tests.Ingest;

public class JsonArrayRecordWriterTests
{
    private static readonly IRecordWriter<AgentOutput> Writer = new JsonArrayRecordWriter<AgentOutput>();

    [Fact]
    public async Task WriteAllAsync_MultipleRecords_WritesSingleIndentedJsonArray()
    {
        var records = new[]
        {
            new AgentOutput(NextMessage: null, new NextAction("start_cadence", "welcome", null)),
            new AgentOutput(NextMessage: null, new NextAction("follow_up_in_days", "check_in", 3)),
        };
        using var writer = new StringWriter();

        await Writer.WriteAllAsync(writer, records);

        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(2, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task WriteAllAsync_Record_SerializesWithSnakeCaseFieldNames()
    {
        var records = new[] { new AgentOutput(NextMessage: null, new NextAction("start_cadence", "welcome", null)) };
        using var writer = new StringWriter();

        await Writer.WriteAllAsync(writer, records);

        Assert.Contains("\"next_action\"", writer.ToString());
        Assert.Contains("\"next_message\"", writer.ToString());
    }

    [Fact]
    public async Task WriteAllAsync_Record_IsIndentedForHumanReadability()
    {
        var records = new[] { new AgentOutput(NextMessage: null, new NextAction("start_cadence", "welcome", null)) };
        using var writer = new StringWriter();

        await Writer.WriteAllAsync(writer, records);

        Assert.Contains(Environment.NewLine, writer.ToString());
    }

    [Fact]
    public async Task WriteAllAsync_NoRecords_WritesEmptyJsonArray()
    {
        using var writer = new StringWriter();

        await Writer.WriteAllAsync(writer, Array.Empty<AgentOutput>());

        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(0, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task WriteAllAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var records = new[] { new AgentOutput(NextMessage: null, new NextAction("start_cadence", "welcome", null)) };
        using var writer = new StringWriter();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Writer.WriteAllAsync(writer, records, cts.Token));
    }
}
