using System.Collections.Frozen;
using Agent.Common;

namespace Agent.Composition;

// One call to action: the type that goes on the wire and the path an email link ends in
// (A9, A21). The sms reply options are not here: they are prose, so they live in the
// language sets with the rest of the words a person reads (D26), and one list in one place
// is the point of a table.
internal sealed record CallToAction(string Type, string LinkPath);

// D25 and playbook step 42: the call-to-action vocabulary in one table. Nothing else in the
// program spells a call-to-action type or a link path. The payload shape stays the channel's
// rule (A10), which is why an unrecognized call to action still gets a payload; its wording
// comes from the record's language set.
internal static class CallToActionCatalog
{
    // A9: the call to action a record with no primary_cta gets, and the payload every
    // unrecognized one falls back to. This is D19's deferred default, landed as the
    // catalog's generic row rather than as a column on the persona and stage rows: no
    // sample shows a record without primary_cta, so a per-row default would be an invented
    // value (A19).
    public static readonly CallToAction Generic = new("reply", "reply");

    private static readonly FrozenDictionary<string, CallToAction> ByPrimaryCta =
        new Dictionary<string, CallToAction>(StringComparer.Ordinal)
        {
            ["book_tour"] = new("schedule_tour", "tour"),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    // O(1): one hash lookup. A9: an absent or blank primary_cta takes the generic row, and a
    // value the table does not name passes through unchanged as the type, carrying the
    // generic payload.
    public static CallToAction Resolve(string? primaryCta) =>
        Presence.IsAbsent(primaryCta)
            ? Generic
            : ByPrimaryCta.TryGetValue(primaryCta!, out CallToAction? cta) ? cta : Generic with { Type = primaryCta! };

    private static readonly FrozenDictionary<string, string> LinkPathByType =
        ByPrimaryCta.Values
            .Append(Generic)
            .ToFrozenDictionary(cta => cta.Type, cta => cta.LinkPath, StringComparer.Ordinal);

    // O(1): one hash lookup. Reverse of Resolve: a cta_type a caller already has (the one
    // that ends up on the wire) maps to its own link path, rather than a caller re-deriving
    // the path from the primary_cta constraint that produced the type - the two can diverge
    // when nothing constrains the type at all (OpenAiMessageComposer, absent primary_cta),
    // so the type actually being sent is the only fact the link path can safely follow.
    // A type the catalog does not name (the model's own invention with no constraint) takes
    // the generic path, the same fallback Resolve gives an unrecognized primary_cta.
    public static string LinkPathForType(string ctaType) =>
        LinkPathByType.TryGetValue(ctaType, out string? linkPath) ? linkPath : Generic.LinkPath;
}
