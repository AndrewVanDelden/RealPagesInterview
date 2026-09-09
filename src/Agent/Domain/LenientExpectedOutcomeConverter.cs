using System.Text.Json;
using System.Text.Json.Serialization;
using Agent.Common;
using Microsoft.Extensions.Logging;

namespace Agent.Domain;

// "expected" is the scoring oracle, not something the agent reads to decide anything
// (DESIGN.md section 2) - its shape on a real hold-out file is not under our control.
// JsonDocument.ParseValue safely consumes the "expected" value off the wire regardless
// of its shape (object, array, scalar, or malformed) without disturbing the outer
// ProspectCase read, then a strict attempt against that captured value falls back to
// null on any parse failure. This keeps ProspectCase's own strict, single-pass
// deserialization (every other field still fails loud) while a single record's
// unrecognized oracle shape cannot take down the rest of the record or the file.
public sealed class LenientExpectedOutcomeConverter : JsonConverter<ExpectedOutcome?>
{
    public override ExpectedOutcome? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            return document.Deserialize<ExpectedOutcome>(options);
        }
        catch (JsonException ex)
        {
            // Step 68: the exception is not attached to the entry. The 'expected' block is
            // the one part of a record that holds a written message - the label's subject
            // and body - and LogLineFormatter appends the whole Exception.ToString(), a
            // parser message and a stack trace, after every entry that carries one.
            AgentLog.CreateLogger(nameof(LenientExpectedOutcomeConverter))
                .LogWarning(
                    "Could not parse the 'expected' field ({ParseFailure}); this record will be treated as unlabeled.",
                    ex.ToRedactedDiagnosticString());
            return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, ExpectedOutcome? value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
