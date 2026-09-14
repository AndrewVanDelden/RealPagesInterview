using Agent.Common;

namespace Agent.Composition;

// The property management system's facts for the properties a run covers, read from
// --property-data. Tour availability, prices and renewal offers are facts a leasing message can
// only state when this system of record gives them; the model never supplies one.
public sealed record PropertyData(IReadOnlyList<PropertyFacts> Properties)
{
    // O(p) in the properties. The name is compared trimmed and without regard to case, the way
    // people type a property's name; an absent name finds nothing.
    public Option<PropertyFacts> FactsFor(string? propertyName)
    {
        if (Presence.IsAbsent(propertyName))
        {
            return Option<PropertyFacts>.None();
        }

        string wanted = propertyName!.Trim();
        PropertyFacts? found = Properties.FirstOrDefault(facts => string.Equals(facts.PropertyName.Trim(), wanted, StringComparison.OrdinalIgnoreCase));

        return found is null ? Option<PropertyFacts>.None() : Option<PropertyFacts>.Some(found);
    }
}
