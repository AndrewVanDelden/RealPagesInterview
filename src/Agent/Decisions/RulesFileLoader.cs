using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agent.Common;
using Agent.Domain;

namespace Agent.Decisions;

// Reads a rules file, whose catalog and send slots replace the compiled ones whole. The file
// comes from outside the program, so each row is read and checked on its own, and every bad
// row is in the one failure, named by its 1-based place in its array and by its key. A member
// the format does not define, or one stated twice, is refused rather than skipped, so a
// misspelled member cannot silently drop a rule.
public static class RulesFileLoader
{
    private const string NotInRowFormat =
        "does not match the row format (a value of the wrong type, a required member missing, or a member the format does not define).";

    private static readonly JsonSerializerOptions Options = new(AgentJsonOptions.Default)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    // O(n) time and space in the file's length: the text is read whole, then each row once.
    public static Result<DecisionRules> Load(TextReader reader)
    {
        RulesFileContent? content;

        try
        {
            content = JsonSerializer.Deserialize<RulesFileContent>(reader.ReadToEnd(), Options);
        }
        catch (JsonException ex)
        {
            // The parser's message and path quote the file's own text, so the failure carries
            // the exception type and the position only, the way a bad record line does.
            return Result<DecisionRules>.Failure($"Rules file failed to parse: {ex.ToRedactedDiagnosticString()}");
        }

        if (content is null)
        {
            return Result<DecisionRules>.Failure("Rules file holds null, not a rules object.");
        }

        (List<(int Position, ActionCatalogRow Row)> catalogRows, List<string> failures) = ReadCatalogRows(content.ActionCatalog.Rows);
        Result<ActionCatalog> catalog = ActionCatalog.Create(
            ReadRow<GenericActionRow>(content.ActionCatalog.GenericRow, "Generic catalog row"),
            catalogRows);

        if (!catalog.IsSuccess)
        {
            failures.Add(catalog.Error);
        }

        (List<(int Position, SendSlotRow Row)> slotRows, List<string> slotFailures) = ReadSlotRows(content.SendSlots);
        failures.AddRange(slotFailures);
        Result<SendSlotTable> slots = SendSlotTable.Create(slotRows);

        if (!slots.IsSuccess)
        {
            failures.Add(slots.Error);
        }

        return failures.Count == 0
            ? Result<DecisionRules>.Success(new DecisionRules(catalog.Value, slots.Value))
            : Result<DecisionRules>.Failure(string.Join(Environment.NewLine, failures));
    }

    // O(n) time and space in the number of rows. An absent persona or stage reaches the
    // catalog's gate as blank, which names it.
    private static (List<(int Position, ActionCatalogRow Row)> Rows, List<string> Failures) ReadCatalogRows(IReadOnlyList<JsonElement> elements)
    {
        List<(int Position, ActionCatalogRow Row)> rows = [];
        List<string> failures = [];

        for (int index = 0; index < elements.Count; index++)
        {
            int position = index + 1;
            string label = $"Catalog row {position} ({KeyAsWritten(elements[index], "persona", "lifecycle_stage")})";
            Result<RulesFileCatalogRow> read = ReadRow<RulesFileCatalogRow>(elements[index], label);

            if (read.IsSuccess)
            {
                RulesFileCatalogRow entry = read.Value;
                rows.Add((position, new ActionCatalogRow(
                    entry.Persona ?? string.Empty,
                    entry.LifecycleStage ?? string.Empty,
                    Stated(entry.ShortHorizonAction),
                    Stated(entry.LongHorizonAction))));
            }
            else
            {
                failures.Add(read.Error);
            }
        }

        return (rows, failures);
    }

    // O(n) time and space in the number of rows.
    private static (List<(int Position, SendSlotRow Row)> Rows, List<string> Failures) ReadSlotRows(IReadOnlyList<JsonElement> elements)
    {
        List<(int Position, SendSlotRow Row)> rows = [];
        List<string> failures = [];

        for (int index = 0; index < elements.Count; index++)
        {
            int position = index + 1;
            string label = $"Send slot row {position} ({KeyAsWritten(elements[index], "persona", "lifecycle_stage", "channel")})";
            Result<RulesFileSlotRow> read = ReadRow<RulesFileSlotRow>(elements[index], label);
            Result<SendSlotRow> slot = read.IsSuccess ? ToSendSlotRow(read.Value, label) : Result<SendSlotRow>.Failure(read.Error);

            if (slot.IsSuccess)
            {
                rows.Add((position, slot.Value));
            }
            else
            {
                failures.Add(slot.Error);
            }
        }

        return (rows, failures);
    }

    // One row read on its own, so a row that does not match the format fails alone.
    private static Result<TRow> ReadRow<TRow>(JsonElement element, string label)
        where TRow : class
    {
        try
        {
            TRow? row = element.Deserialize<TRow>(Options);

            return row is null ? Result<TRow>.Failure($"{label}: is null.") : Result<TRow>.Success(row);
        }
        catch (JsonException)
        {
            return Result<TRow>.Failure($"{label}: {NotInRowFormat}");
        }
    }

    // A slot needs a channel, a day offset and a local time before the table can judge it, and
    // an absent value is never defaulted: each one missing or malformed is named.
    private static Result<SendSlotRow> ToSendSlotRow(RulesFileSlotRow entry, string label)
    {
        if (entry.Channel is { } channel
            && entry.DaysAfterFloorDay is { } days
            && entry.LocalTime is { } text
            && TryParseLocalTime(text, out TimeOnly localTime))
        {
            return Result<SendSlotRow>.Success(new SendSlotRow(entry.Persona ?? string.Empty, entry.LifecycleStage ?? string.Empty, channel, days, localTime));
        }

        List<string> failures = [];

        if (entry.Channel is null)
        {
            failures.Add($"{label}: channel is absent.");
        }

        if (entry.DaysAfterFloorDay is null)
        {
            failures.Add($"{label}: day offset is absent.");
        }

        if (entry.LocalTime is null)
        {
            failures.Add($"{label}: local time is absent.");
        }
        else if (!TryParseLocalTime(entry.LocalTime, out _))
        {
            failures.Add($"{label}: local time is not a 24-hour HH:mm time.");
        }

        return Result<SendSlotRow>.Failure(string.Join(Environment.NewLine, failures));
    }

    private static bool TryParseLocalTime(string text, out TimeOnly localTime) =>
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out localTime);

    private static Option<NextAction> Stated(NextAction? action) =>
        action is null ? Option<NextAction>.None() : Option<NextAction>.Some(action);

    // A row's key as the file wrote it, trimmed and lowercased the way the tables compare keys.
    // It is read before the row itself so a row that cannot be read is still named; a member
    // that is absent or not a string reads as empty.
    private static string KeyAsWritten(JsonElement row, params string[] memberNames) =>
        string.Join("/", memberNames.Select(name => StringMember(row, name)));

    private static string StringMember(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out JsonElement member) && member.ValueKind == JsonValueKind.String
            ? member.GetString()!.Trim().ToLowerInvariant()
            : string.Empty;
}
