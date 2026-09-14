using Agent.Common;

namespace Agent.Composition;

// One property's facts as the property management system states them. Every fact is optional:
// a fact the system does not give is one no message states.
public sealed record PropertyFacts(
    string PropertyName,
    string? TourAvailability = null,
    IReadOnlyList<StartingPrice>? StartingPrices = null,
    IReadOnlyList<RenewalOffer>? RenewalOffers = null,
    string? ExtendedTourHours = null)
{
    // The offer id a record states identifies its offer; a record that states none is matched by
    // its unit. Both are identifiers, so they are compared trimmed and ordinally. O(o) in the
    // offers.
    public Option<RenewalOffer> RenewalOfferFor(string? unit, string? renewalOfferId)
    {
        RenewalOffer? found = Presence.IsAbsent(renewalOfferId)
            ? Presence.IsAbsent(unit) ? null : RenewalOffers?.FirstOrDefault(offer => SameIdentifier(offer.Unit, unit!))
            : RenewalOffers?.FirstOrDefault(offer => SameIdentifier(offer.OfferId, renewalOfferId!));

        return found is null ? Option<RenewalOffer>.None() : Option<RenewalOffer>.Some(found);
    }

    // Folded through PropertyLink.FoldDashes before comparing: a unit is the same identifier
    // whichever dash character it was typed with, the same rule the link segment already
    // follows for the same field.
    private static bool SameIdentifier(string? stated, string wanted) =>
        stated is not null && string.Equals(PropertyLink.FoldDashes(stated.Trim()), PropertyLink.FoldDashes(wanted.Trim()), StringComparison.Ordinal);
}
