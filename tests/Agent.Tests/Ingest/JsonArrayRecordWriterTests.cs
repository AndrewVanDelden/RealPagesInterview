using System.Text.Json;
using Agent.Common;
using Agent.Domain;
using Agent.Ingest;
using Xunit;

namespace Agent.Tests.Ingest;

public class JsonArrayRecordWriterTests
{
    // The file format before rows were written one at a time: the whole list serialized in one
    // call. A reader of --output or --replay must not be able to tell the two apart.
    private static readonly JsonSerializerOptions WholeArrayOptions = new(AgentJsonOptions.Default) { WriteIndented = true };

    // An apostrophe and a non-ASCII letter, both of which the default encoder would escape, so a
    // writer that dropped the relaxed encoder would not match the whole-array form.
    private static readonly AgentOutput Sent = new(
        new NextMessage(CommunicationChannel.Email, DateTimeOffset.Parse("2025-12-09T10:00:00-06:00"), "Tour Oak Ridge", "Hi Taylor, it's Café day. Reply STOP to opt out.", new Cta("schedule_tour", null, new Uri("https://oakridge.example/tour"))),
        new NextAction("follow_up_in_days", null, 3));

    private static readonly AgentOutput Suppressed = new(new NextMessage(CommunicationChannel.None), new NextAction("no_op", Reason: "no_contact_consent"));

    private static async Task<string> WriteRowsAsync(IReadOnlyList<AgentOutput> rows)
    {
        using var target = new StringWriter();
        var writer = new JsonArrayRecordWriter<AgentOutput>(target);

        await writer.BeginAsync();
        foreach (AgentOutput row in rows)
        {
            await writer.WriteRowAsync(row);
        }

        await writer.EndAsync();
        return target.ToString();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RowsWrittenOneAtATime_AreByteIdenticalToTheWholeArraySerializedAtOnce(int rowCount)
    {
        AgentOutput[] rows = new[] { Sent, Suppressed }.Take(rowCount).ToArray();

        string written = await WriteRowsAsync(rows);

        Assert.Equal(JsonSerializer.Serialize(rows, WholeArrayOptions), written);
    }

    [Fact]
    public async Task WriteRowAsync_BeforeTheArrayEnds_TheRowIsAlreadyOnTheTarget()
    {
        using var target = new StringWriter();
        var writer = new JsonArrayRecordWriter<AgentOutput>(target);

        await writer.BeginAsync();
        await writer.WriteRowAsync(Sent);

        string soFar = target.ToString();
        Assert.StartsWith("[", soFar, StringComparison.Ordinal);
        Assert.Contains("\"follow_up_in_days\"", soFar, StringComparison.Ordinal);
        Assert.DoesNotContain("]", soFar.TrimEnd()[^1..], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteRowAsync_TargetIsABufferedStreamWriter_TheRowReachesTheFileBeforeDisposal()
    {
        // StringWriter above proves the row reaches the TextWriter, but a StreamWriter over a
        // file keeps its own ~1024-character buffer beneath that: a FlushAsync that stops at
        // target.WriteAsync leaves the row sitting in the StreamWriter's buffer, never reaching
        // the file, until something else disposes or flushes it. Production writes through
        // exactly this StreamWriter-over-file shape (CliRunner), so only a real file, opened a
        // second time while the first handle is still open, can show whether the bytes landed.
        string path = Path.GetTempFileName();
        try
        {
            await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            using var target = new StreamWriter(fileStream);
            var writer = new JsonArrayRecordWriter<AgentOutput>(target);

            await writer.BeginAsync();
            await writer.WriteRowAsync(Sent);

            using var readStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(readStream);
            string onDisk = await reader.ReadToEndAsync();

            Assert.Contains("\"follow_up_in_days\"", onDisk, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MultipleRows_WritesSingleIndentedJsonArray()
    {
        string written = await WriteRowsAsync([Sent, Suppressed]);

        using JsonDocument document = JsonDocument.Parse(written);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(2, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Row_SerializesWithSnakeCaseFieldNames()
    {
        string written = await WriteRowsAsync([Suppressed]);

        Assert.Contains("\"next_action\"", written, StringComparison.Ordinal);
        Assert.Contains("\"next_message\"", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Row_IsIndentedForHumanReadability()
    {
        string written = await WriteRowsAsync([Suppressed]);

        Assert.Contains(written.Split('\n'), line => line.StartsWith("  {", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoRows_WritesEmptyJsonArray()
    {
        string written = await WriteRowsAsync([]);

        using JsonDocument document = JsonDocument.Parse(written);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(0, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task WriteRowAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        using var target = new StringWriter();
        var writer = new JsonArrayRecordWriter<AgentOutput>(target);
        await writer.BeginAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteRowAsync(Suppressed, cts.Token));
    }
}
