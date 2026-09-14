using System.Collections.Frozen;
using Agent.Common;

namespace Agent.Composition;

// The property management system's facts for the properties a run covers, read from
// --property-data. Tour availability, prices and renewal offers are facts a leasing message can
// only state when this system of record gives them; the model never supplies one.
public sealed record PropertyData(IReadOnlyList<PropertyFacts> Properties)
{
    // Built once, here, so every FactsFor call is a hash lookup rather than a scan: a live run
    // calls it twice per record (LeasingMessageAgent, then the composer), once more per compose
    // retry. The first property with a given name wins a duplicate, the way a linear first-match
    // would have; PropertyDataLoader already refuses a file that names one twice.
    private readonly FrozenDictionary<string, PropertyFacts> byName = BuildIndex(Properties);

    // O(1): one hash lookup against the index built at construction. The name is compared
    // trimmed and without regard to case, the way people type a property's name; an absent name
    // finds nothing.
    public Option<PropertyFacts> FactsFor(string? propertyName)
    {
        if (Presence.IsAbsent(propertyName))
        {
            return Option<PropertyFacts>.None();
        }

        return byName.TryGetValue(propertyName!.Trim(), out PropertyFacts? found)
            ? Option<PropertyFacts>.Some(found)
            : Option<PropertyFacts>.None();
    }

    // O(p) in the properties, once.
    private static FrozenDictionary<string, PropertyFacts> BuildIndex(IReadOnlyList<PropertyFacts> properties)
    {
        var index = new Dictionary<string, PropertyFacts>(properties.Count, StringComparer.OrdinalIgnoreCase);

        foreach (PropertyFacts facts in properties)
        {
            index.TryAdd(facts.PropertyName.Trim(), facts);
        }

        return index.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
