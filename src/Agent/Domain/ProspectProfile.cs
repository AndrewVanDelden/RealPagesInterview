namespace Agent.Domain;

public sealed record ProspectProfile(
    string FirstName,
    string? CityInterest,
    IReadOnlyList<string>? AmenityInterest)
{
    // Normalized, always-non-null views of the two interest fields, used by every
    // composer that describes stated interest - the null/empty-vs-non-empty guard lives
    // here once instead of being duplicated at each call site.
    public IReadOnlyList<string> Amenities => AmenityInterest is { Count: > 0 } amenities ? amenities : [];

    public string City => CityInterest is { Length: > 0 } city ? city : string.Empty;
}
