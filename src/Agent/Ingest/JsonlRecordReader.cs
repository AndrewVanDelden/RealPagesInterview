using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agent.Common;
using Agent.Domain;

namespace Agent.Ingest;

public sealed class JsonlRecordReader
{
    // The three members a record must carry; a line missing one is an error row naming it.
    // The deserializer's own message would name it too, but that message quotes the record's
    // text and is never reported (step 68), so the names come from this program's own schema
    // rather than from anything the record wrote.
    //
    // Read from ProspectCase's own [JsonConstructor] rather than spelled out a second time
    // (A17): the constructor's parameters are what RespectRequiredConstructorParameters
    // actually enforces, so this list can never drift from the contract it describes.
    private static readonly string[] RequiredMembers = typeof(ProspectCase)
        .GetConstructors()
        .Single(constructor => constructor.GetCustomAttribute<JsonConstructorAttribute>() is not null)
        .GetParameters()
        .Select(parameter => AgentJsonOptions.Default.PropertyNamingPolicy!.ConvertName(parameter.Name!))
        .ToArray();

    // One result per non-blank line, in file order, read only as the caller asks for the next
    // one, so a batch holds the record it is on rather than the file. A line that does not
    // parse is a failure row naming its 1-based line number; every other line still comes back
    // parsed (HB: failures are reported per record, never per batch).
    // O(n) time in the input lines over a full enumeration; O(one line) space.
    public IEnumerable<Result<ProspectCase>> ReadEach(TextReader reader)
    {
        int lineNumber = 0;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            yield return ReadLine(line, lineNumber);
        }
    }

    // The same results held as a list, for a caller that pairs them by position and so needs
    // them all at once. O(n) time and O(n) space in the input lines.
    public IReadOnlyList<Result<ProspectCase>> ReadAll(TextReader reader) => [.. ReadEach(reader)];

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
