using System.Text.Json.Serialization;

namespace Agent.Domain;

// D2: one record for every action shape. Type is always present; the other members are
// each used by some shapes and omitted from the wire when null, the way the oracle
// spells them.
public sealed record NextAction(
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Value = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null);
