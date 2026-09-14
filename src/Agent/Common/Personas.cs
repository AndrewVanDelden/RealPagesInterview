namespace Agent.Common;

// A record's persona is optional free text. The one question several rules ask of it is whether the
// record is a prospect, since the evidence shows a prospect's cadences, tour offers and move timeline
// and nothing of the kind for anyone else.
public static class Personas
{
    private const string Prospect = "prospect";

    // O(n) in the persona's length: a trim and one comparison.
    public static bool IsProspect(string? persona) =>
        persona is not null && string.Equals(persona.Trim(), Prospect, StringComparison.OrdinalIgnoreCase);
}
