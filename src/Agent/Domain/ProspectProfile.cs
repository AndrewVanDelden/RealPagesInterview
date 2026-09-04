namespace Agent.Domain;

public sealed record ProspectProfile(
    string FirstName,
    string? CityInterest,
    IReadOnlyList<string>? AmenityInterest)
{
    public bool HasAmenityInterest => AmenityInterest is { Count: > 0 };

    public bool HasCityInterest => CityInterest is { Length: > 0 };
}
