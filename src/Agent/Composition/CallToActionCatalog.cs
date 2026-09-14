using System.Collections.Frozen;
using Agent.Common;

namespace Agent.Composition;

// One call to action: the type that goes on the wire, the path an email link ends in (A9, A21),
// the purpose the model is told the message serves, a decision code owns like the type and null
// for a call to action no label shows, and the kinds of property fact that serve it, null when none
// does. The sms reply options are not here: they are prose, so they live in the language sets with
// the rest of the words a person reads, and one list in one place is the point of a table.
internal sealed record CallToAction(string Type, string LinkPath, string? Purpose = null, FrozenSet<PropertyFactKind>? FactKinds = null);

// Playbook step 42: the call-to-action vocabulary in one table. Nothing else in the
// program spells a call-to-action type or a link path. The payload shape stays the channel's
// rule (A10), which is why an unrecognized call to action still gets a payload; its wording
// comes from the record's language set.
internal static class CallToActionCatalog
{
    // A9: the call to action a record with no primary_cta gets when its persona and stage
    // have no default below, and the payload every unrecognized one falls back to.
    public static readonly CallToAction Generic = new("reply", "reply");

    // The tour invitation's wire type, named once: its sms reply options are the planned tour slots.
    public const string TourType = "schedule_tour";

    // Sample 2's link, https://oakridge.example/tour, gives the tour its path.
    // A tour invitation can carry the property's tour availability, its extended tour hours and
    // its starting prices; the composer gives the last two only to a record whose input calls for
    // them. Hold-out prospect_welcome_day0 uses the availability and
    // prospect_cancellation_manager_cross_sell all three.
    private static readonly CallToAction ScheduleTour = new(
        TourType,
        "tour",
        "invite the prospect to book a tour",
        new[] { PropertyFactKind.TourAvailability, PropertyFactKind.ExtendedTourHours, PropertyFactKind.StartingPrice }.ToFrozenSet());

    // The rows a record's primary_cta names: a wire type that differs from it, a link path the
    // generic row does not have, or both. The two sms rows show no link in their labels, so they
    // keep the generic path; the resident email labels give the welcome, loyalty and renewal
    // paths, the renewal one under the record's own unit (A26).
    private static readonly FrozenDictionary<string, CallToAction> ByPrimaryCta =
        new Dictionary<string, CallToAction>(StringComparer.Ordinal)
        {
            ["book_tour"] = ScheduleTour,
            // prospect_no_show_reengage
            ["reschedule_tour"] = new("reschedule", Generic.LinkPath, "offer to reschedule the tour the prospect missed"),

            // resident_renewal_undecided_followup
            ["reply_intent"] = new("intent_capture", Generic.LinkPath, "ask directly whether the resident wants to renew their unit"),

            // resident_welcome_day0
            ["get_started"] = new("get_started", "welcome", "ask the resident to complete each feature in features_enablement before move-in"),

            // resident_loyalty_engage
            ["enroll_loyalty"] = new("enroll_loyalty", "loyalty", "invite the resident to enroll in the loyalty program"),

            // resident_renewal_90day_notice
            ["review_renewal"] = new(
                "review_renewal",
                $"renewal/{PropertyLink.UnitSegment}",
                "ask the resident to review their renewal offer",
                new[] { PropertyFactKind.RenewalOffer }.ToFrozenSet()),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    // A record with no primary_cta at these personas and stages gets the stage's own call to
    // action, keyed the way the action catalog is. Evidence: hold-out
    // prospect_consent_block_sms_fallback_email is prospect/new and labeled schedule_tour;
    // resident_renewal_details_branch_email is labeled review_renewal_details with the details
    // page under the record's unit.
    private static readonly FrozenDictionary<(string Persona, string LifecycleStage), CallToAction> ByPersonaAndStage =
        new Dictionary<(string Persona, string LifecycleStage), CallToAction>
        {
            [("prospect", "new")] = ScheduleTour,
            [("resident", "renewal_details_requested")] = new(
                "review_renewal_details",
                $"renewal/{PropertyLink.UnitSegment}/details",
                "give the resident their renewal details and ask whether they are ready to continue"),
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
