using System.Text.Json;
using Agent.Common;
using Agent.Domain;

namespace Agent.Ingest;

public sealed class JsonlRecordReader
{
    // D1: the three members a record must carry, spelled once. Until step 68's redaction the
    // error row named a missing one only because the deserializer's own message did; that
    // message can no longer be reported, so the guarantee is stated here instead, from this
    // program's own schema names rather than from anything the record wrote.
    private static readonly string[] RequiredMembers = ["task_id", "consent", "channel_preferences"];

    // One result per non-blank line, in file order. A line that does not parse is a
    // failure row naming its 1-based line number; every other line still comes back
    // parsed (HB: failures are reported per record, never per batch).
    // O(n) time and O(n) space in the number of input lines: every result stays resident
    // until the caller takes the list.
    public IReadOnlyList<Result<ProspectCase>> ReadAll(TextReader reader)
    {
        var results = new List<Result<ProspectCase>>();
        int lineNumber = 0;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            results.Add(ReadLine(line, lineNumber));
        }

        return results;
    }

    private static Result<ProspectCase> ReadLine(string line, int lineNumber)
    {
        try
        {
            ProspectCase? prospectCase = JsonSerializer.Deserialize<ProspectCase>(line, AgentJsonOptions.Default);

            return prospectCase is null
                ? Result<ProspectCase>.Failure($"Line {lineNumber} deserialized to null.")
                : Result<ProspectCase>.Success(prospectCase);
        }
        catch (JsonException ex)
        {
            // Step 68: this failure is reported to the log and to stderr by CliRunner, so it
            // carries the line number and the position within it, never the parser's own
            // message. That message names the offending character of the record, and its
            // path names the member it was reading - which, for a member the record types do
            // not declare, is a name the record itself authored.
            return Result<ProspectCase>.Failure(
                $"Line {lineNumber} failed to parse: {ex.ToRedactedDiagnosticString()}{DescribeMissingRequiredMembers(line)}");
        }
    }

    // O(m) in the line's length, and only on a line that already failed: one more pass to say
    // which required members it does not carry. A line too malformed to re-read, or one whose
    // root is not an object, names none - its failure is a syntax failure, not a missing
    // member, and the position already located it.
    private static string DescribeMissingRequiredMembers(string line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return string.Empty;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            string[] missing = RequiredMembers
                .Where(member => !document.RootElement.TryGetProperty(member, out _))
                .ToArray();

            return missing.Length == 0 ? string.Empty : $" Missing required member(s): {string.Join(", ", missing)}.";
        }
    }
}
