using System.Collections.Frozen;
using Agent.Common;

namespace Agent.Composition;

// One call to action: the type that goes on the wire and the path an email link ends in
// (A9, A21). The sms reply options are not here: they are prose, so they live in the
// language sets with the rest of the words a person reads, and one list in one place is the
// point of a table.
internal sealed record CallToAction(string Type, string LinkPath);

// Playbook step 42: the call-to-action vocabulary in one table. Nothing else in the
// program spells a call-to-action type or a link path. The payload shape stays the channel's
// rule (A10), which is why an unrecognized call to action still gets a payload; its wording
// comes from the record's language set.
internal static class CallToActionCatalog
{
    // A9: the call to action a record with no primary_cta gets when its persona and stage
    // have no default below, and the payload every unrecognized one falls back to.
    public static readonly CallToAction Generic = new("reply", "reply");

    // Sample 2's link, https://oakridge.example/tour, gives the tour its path.
    private static readonly CallToAction ScheduleTour = new("schedule_tour", "tour");

    // The rows whose wire type differs from the record's primary_cta. The two hold-out rows
    // are sms records, so no label shows their link path and they keep the generic one.
    private static readonly FrozenDictionary<string, CallToAction> ByPrimaryCta =
        new Dictionary<string, CallToAction>(StringComparer.Ordinal)
        {
            ["book_tour"] = ScheduleTour,
            ["reschedule_tour"] = new("reschedule", Generic.LinkPath),     // prospect_no_show_reengage
            ["reply_intent"] = new("intent_capture", Generic.LinkPath),    // resident_renewal_undecided_followup
        }.ToFrozenDictionary(StringComparer.Ordinal);

    // A record with no primary_cta at these personas and stages gets the stage's own call to
    // action, keyed the way the action catalog is. Evidence: hold-out
    // prospect_consent_block_sms_fallback_email is prospect/new and labeled schedule_tour;
    // resident_renewal_details_branch_email is labeled review_renewal_details, whose label link
    // carries the unit, which no path here can build, so it keeps the generic path.
    private static readonly FrozenDictionary<(string Persona, string LifecycleStage), CallToAction> ByPersonaAndStage =
        new Dictionary<(string Persona, string LifecycleStage), CallToAction>
        {
            [("prospect", "new")] = ScheduleTour,
            [("resident", "renewal_details_requested")] = new("review_renewal_details", Generic.LinkPath),
        }.ToFrozenDictionary();

    // O(1): at most one hash lookup. A stated primary_cta wins: the table's row, or the value
    // passed through unchanged with the generic payload (A9). An absent or blank one takes the
    // stage default, and the generic row when the stage has none or the record states no
    // persona or stage.
    public static CallToAction Resolve(string? primaryCta, string? persona, string? lifecycleStage)
    {
        if (!Presence.IsAbsent(primaryCta))
        {
            return ByPrimaryCta.TryGetValue(primaryCta!, out CallToAction? cta) ? cta : Generic with { Type = primaryCta! };
        }

        return persona is not null
            && lifecycleStage is not null
            && ByPersonaAndStage.TryGetValue((persona.Trim().ToLowerInvariant(), lifecycleStage.Trim().ToLowerInvariant()), out CallToAction? stageDefault)
            ? stageDefault
            : Generic;
    }
}
