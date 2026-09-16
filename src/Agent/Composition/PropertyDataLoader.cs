using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agent.Common;

namespace Agent.Composition;

// Reads a property data file, the stand-in for the property management system's feed. The file
// comes from outside the program, so each property is read and checked on its own, every bad
// property is in the one failure, named by its 1-based place and its name, and a member the
// format does not define is refused rather than skipped, so a misspelled fact cannot silently
// vanish.
public static class PropertyDataLoader
{
    private const string NotInFormat =
        "does not match the property format (a value of the wrong type, a required member missing, or a member the format does not define).";

    private static readonly JsonSerializerOptions Options = new(AgentJsonOptions.Default)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    // O(n) time and space in the file's length: the text is read whole, then each property once.
    public static Result<PropertyData> Load(TextReader reader)
    {
        PropertyDataFileContent? content;

        try
        {
            content = JsonSerializer.Deserialize<PropertyDataFileContent>(reader.ReadToEnd(), Options);
        }
        catch (JsonException ex)
        {
            // The parser's message and path quote the file's own text, so the failure carries the
            // exception type and the position only.
            return Result<PropertyData>.Failure($"Property data file failed to parse: {ex.ToRedactedDiagnosticString()}");
        }

        if (content is null)
        {
            return Result<PropertyData>.Failure("Property data file holds null, not a property data object.");
        }

        var properties = new List<PropertyFacts>(content.Properties.Count);
        var failures = new List<string>();
        var positionsByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < content.Properties.Count; index++)
        {
            int position = index + 1;
            string name = NameAsWritten(content.Properties[index]);
            string label = name.Length > 0 ? $"Property {position} ({name})" : $"Property {position}";
            PropertyFileEntry? entry = ReadEntry(content.Properties[index]);

            if (entry is null)
            {
                failures.Add($"{label}: {NotInFormat}");
                continue;
            }

            if (Presence.IsAbsent(entry.PropertyName))
            {
                failures.Add($"{label}: property_name is blank.");
                continue;
            }

            if (!positionsByName.TryAdd(entry.PropertyName!.Trim(), position))
            {
                failures.Add($"{label}: duplicates property {positionsByName[entry.PropertyName.Trim()]}.");
            }

            failures.AddRange(PriceFailures(label, entry.StartingPrices ?? []));
            failures.AddRange(OfferFailures(label, entry.RenewalOffers ?? []));
            failures.AddRange(CalendarFailures(label, entry));
            properties.Add(new PropertyFacts(entry.PropertyName, entry.TourAvailability, entry.StartingPrices, entry.RenewalOffers, entry.ExtendedTourHours, CalendarOf(entry)));
        }

        return failures.Count == 0
            ? Result<PropertyData>.Success(new PropertyData(properties))
            : Result<PropertyData>.Failure(string.Join(Environment.NewLine, failures));
    }

    // One property read on its own, so a property that does not match the format fails alone.
    private static PropertyFileEntry? ReadEntry(JsonElement element)
    {
        try
        {
            return element.Deserialize<PropertyFileEntry>(Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A null inside a list is what JSON allows whatever the element type says, so it is named
    // rather than reached. O(k) in the prices.
    private static IEnumerable<string> PriceFailures(string label, IReadOnlyList<StartingPrice?> prices) =>
        prices.Select((price, index) => price switch
        {
            null => $"{label}: starting price {index + 1} is null.",
            { TotalMonthlyPrice: <= 0 } => $"{label}: starting price {index + 1} total_monthly_price {price.TotalMonthlyPrice.ToString(CultureInfo.InvariantCulture)} must be positive.",
            _ => null,
        }).OfType<string>();

    // O(o) in the offers.
    private static IEnumerable<string> OfferFailures(string label, IReadOnlyList<RenewalOffer?> offers) =>
        offers.SelectMany((offer, index) => OfferFailures($"{label}: renewal offer {index + 1}", offer));

    private static IEnumerable<string> OfferFailures(string offerLabel, RenewalOffer? offer)
    {
        if (offer is null)
        {
            yield return $"{offerLabel} is null.";
            yield break;
        }

        if (Presence.IsAbsent(offer.Unit) && Presence.IsAbsent(offer.OfferId))
        {
            yield return $"{offerLabel} names neither a unit nor an offer_id.";
        }

        if (offer.PriceHoldDays is { } days && days <= 0)
        {
            yield return $"{offerLabel} price_hold_days {days} must be positive.";
        }
    }

    // A calendar that could offer no slot, or the wrong one, is refused: an empty list of days, a day
    // that is not a weekday's name, a time not written as 24-hour hours and minutes, and a null closed
    // date. O(d + c) in the days and closed dates.
    private static IEnumerable<string> CalendarFailures(string label, PropertyFileEntry entry)
    {
        if (entry.TourDays is { Count: 0 })
        {
            yield return $"{label}: tour_days is empty, so no tour could be offered.";
        }

        IReadOnlyList<string?> days = entry.TourDays ?? [];

        for (int index = 0; index < days.Count; index++)
        {
            if (!TryReadWeekday(days[index], out _))
            {
                yield return $"{label}: tour day {index + 1} is not a weekday name.";
            }
        }

        if (entry.TourTime is not null && !TryReadTourTime(entry.TourTime, out _))
        {
            yield return $"{label}: tour_time must be 24-hour HH:mm.";
        }

        IReadOnlyList<DateOnly?> closedDates = entry.TourClosedDates ?? [];

        for (int index = 0; index < closedDates.Count; index++)
        {
            if (closedDates[index] is null)
            {
                yield return $"{label}: tour closed date {index + 1} is null.";
            }
        }
    }

    // The calendar the file states, or none when the property states no calendar member. Only the
    // readable members are kept; CalendarFailures has already refused the file for any other.
    private static TourCalendar? CalendarOf(PropertyFileEntry entry)
    {
        if (entry.TourDays is null && entry.TourTime is null && entry.TourClosedDates is null)
        {
            return null;
        }

        IReadOnlyList<DayOfWeek>? openDays = entry.TourDays is null
            ? null
            : [.. entry.TourDays.Select(name => TryReadWeekday(name, out DayOfWeek day) ? (DayOfWeek?)day : null).OfType<DayOfWeek>()];
        TimeOnly? tourTime = entry.TourTime is not null && TryReadTourTime(entry.TourTime, out TimeOnly time) ? time : null;
        IReadOnlyList<DateOnly>? closedDates = entry.TourClosedDates is null ? null : [.. entry.TourClosedDates.OfType<DateOnly>()];

        return new TourCalendar(openDays, tourTime, closedDates);
    }

    // A weekday's English name in any case. Letters only, because Enum.TryParse would also accept a
    // number such as "1" as a weekday.
    private static bool TryReadWeekday(string? name, out DayOfWeek day)
    {
        day = default;
        return name is not null && name.All(char.IsLetter) && Enum.TryParse(name, ignoreCase: true, out day);
    }

    private static bool TryReadTourTime(string text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    // A property's name as the file wrote it, trimmed and lowercased, read before the property
    // itself so a property that cannot be read is still named.
    private static string NameAsWritten(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty("property_name", out JsonElement name)
        && name.ValueKind == JsonValueKind.String
            ? name.GetString()!.Trim().ToLowerInvariant()
            : string.Empty;
}

// The file: one list of properties, each held as raw JSON and read on its own.
internal sealed record PropertyDataFileContent(IReadOnlyList<JsonElement> Properties);

// A property as the file states it. The name may be absent here so the loader can name it.
internal sealed record PropertyFileEntry(
    string? PropertyName = null,
    string? TourAvailability = null,
    IReadOnlyList<StartingPrice>? StartingPrices = null,
    IReadOnlyList<RenewalOffer>? RenewalOffers = null,
    string? ExtendedTourHours = null,
    IReadOnlyList<string?>? TourDays = null,
    string? TourTime = null,
    IReadOnlyList<DateOnly?>? TourClosedDates = null);
