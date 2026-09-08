using System.Collections.Frozen;
using Agent.Common;

namespace Agent.Composition;

// One call to action: the type that goes on the wire, the reply options an sms enumerates,
// and the path an email link ends in (A9, A10, A21).
internal sealed record CallToAction(string Type, IReadOnlyList<string> SmsOptions, string LinkPath);

// D25 and playbook step 42: the call-to-action vocabulary and its payload in one table.
// Nothing else in the program spells a call-to-action type, an option list or a link path.
// The payload content lives here because no input field states it; the payload shape stays
// the channel's rule (A10), which is why an unrecognized call to action still gets one.
internal static class CallToActionCatalog
{
    // A9: the call to action a record with no primary_cta gets, and the payload every
    // unrecognized one falls back to. This is D19's deferred default, landed as the
    // catalog's generic row rather than as a column on the persona and stage rows: no
    // sample shows a record without primary_cta, so a per-row default would be an invented
    // value (A19).
    public static readonly CallToAction Generic = new("reply", ["a question", "a tour"], "reply");

    private static readonly FrozenDictionary<string, CallToAction> ByPrimaryCta =
        new Dictionary<string, CallToAction>(StringComparer.Ordinal)
        {
            ["book_tour"] = new("schedule_tour", ["Thu", "Fri"], "tour"),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    // O(1): one hash lookup. A9: an absent or blank primary_cta takes the generic row, and a
    // value the table does not name passes through unchanged as the type, carrying the
    // generic payload.
    public static CallToAction Resolve(string? primaryCta) =>
        Presence.IsAbsent(primaryCta)
            ? Generic
            : ByPrimaryCta.TryGetValue(primaryCta!, out CallToAction? cta) ? cta : Generic with { Type = primaryCta! };
}
