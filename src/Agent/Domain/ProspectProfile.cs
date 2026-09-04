namespace Agent.Domain;

public sealed record ProspectProfile(
    string FirstName,
    string? CityInterest,
    IReadOnlyList<string>? AmenityInterest);
