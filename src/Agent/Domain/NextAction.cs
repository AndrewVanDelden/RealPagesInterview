using System.Text.Json.Serialization;

namespace Agent.Domain;

// One record for every action shape. Type is always present; the other members are
// each used by some shapes and omitted from the wire when null, the way the oracle
// spells them: InDays is a scheduled reminder's day count, and Mapping names the action each
// reply leads to.
public sealed record NextAction(
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Value = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? InDays = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Mapping = null)
{
    // A mapping is a set of reply-to-action entries, so two actions with the same entries are the
    // same action whatever order they were written in; the equality a record generates would
    // compare the two dictionaries by reference. O(m) in the mapping's entries.
    public bool Equals(NextAction? other) =>
        other is not null
        && Type == other.Type
        && Name == other.Name
        && Value == other.Value
        && Reason == other.Reason
        && InDays == other.InDays
        && MappingsEqual(Mapping, other.Mapping);

    // The mapping's count stands in for its entries, which equal actions share whatever order
    // they were written in.
    public override int GetHashCode() => HashCode.Combine(Type, Name, Value, Reason, InDays, Mapping?.Count);

    // Both absent, or the same entries. O(m) in the entries.
    internal static bool MappingsEqual(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string>? right) =>
        left is null || right is null
            ? left is null && right is null
            : left.Count == right.Count && left.All(entry => right.TryGetValue(entry.Key, out string? target) && target == entry.Value);
}
