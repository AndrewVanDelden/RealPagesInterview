using Agent.Domain;
using Agent.Ingest;
using Xunit;

namespace Agent.Tests.Ingest;

public class JsonlRecordWriterTests
{
    private static readonly IRecordWriter<AgentOutput> Writer = new JsonlRecordWriter<AgentOutput>();

    [Fact]
    public void WriteAll_MultipleRecords_WritesOneJsonLinePerRecord()
    {
        var records = new[]
        {
            new AgentOutput(NextMessage: null, new NextAction("start_cadence", "welcome", null)),
            new AgentOutput(NextMessage: null, new NextAction("follow_up_in_days", "check_in", 3)),
        };
        using var writer = new StringWriter();

        Writer.WriteAll(writer, records);

        string[] lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void WriteAll_Record_SerializesWithSnakeCaseFieldNames()
    {
        var records = new[] { new AgentOutput(NextMessage: null, new NextAction("start_cadence", "welcome", null)) };
        using var writer = new StringWriter();

        Writer.WriteAll(writer, records);

        Assert.Contains("\"next_action\"", writer.ToString());
        Assert.Contains("\"next_message\"", writer.ToString());
    }

    [Fact]
    public void WriteAll_NoRecords_WritesNothing()
    {
        using var writer = new StringWriter();

        Writer.WriteAll(writer, []);

        Assert.Equal(string.Empty, writer.ToString());
    }
}
