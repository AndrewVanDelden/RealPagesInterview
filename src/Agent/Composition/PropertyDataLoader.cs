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
            properties.Add(new PropertyFacts(entry.PropertyName, entry.TourAvailability, entry.StartingPrices, entry.RenewalOffers, entry.ExtendedTourHours));
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
    string? ExtendedTourHours = null);
