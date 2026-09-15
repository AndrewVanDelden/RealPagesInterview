using Agent.Common;
using Agent.Composition;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Composition;

// A record's property facts are found by its property name, compared the way people type
// names, and its renewal offer by the offer id it states, or by its unit when it states none.
public class PropertyDataTests
{
    [Theory]
    [InlineData("Oak Ridge Apartments")]
    [InlineData("  oak ridge apartments ")]
    public void FactsFor_TheRecordsPropertyName_FindsItsFacts(string propertyName)
    {
        Option<PropertyFacts> facts = SamplePropertyData.OakRidge().FactsFor(propertyName);

        Assert.True(facts.HasValue);
        Assert.Equal(SamplePropertyData.TourAvailability, facts.Value.TourAvailability);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("Lakeview Commons")]
    public void FactsFor_NoOrAnotherPropertyName_FindsNothing(string? propertyName)
    {
        Assert.False(SamplePropertyData.OakRidge().FactsFor(propertyName).HasValue);
    }

    [Theory]
    [InlineData(null, "REN‑A204‑2026", true)]
    [InlineData("B‑118", "REN‑A204‑2026", true)]
    [InlineData("A‑204", null, true)]
    [InlineData(" A‑204 ", "  ", true)]
    [InlineData("A‑204", "REN-OTHER", false)]
    [InlineData("B‑118", null, false)]
    [InlineData(null, null, false)]
    public void RenewalOfferFor_TheOfferIdTheRecordStatesOrElseItsUnit_FindsTheOffer(string? unit, string? offerId, bool found)
    {
        PropertyFacts facts = SamplePropertyData.OakRidge().Properties[0];

        Option<RenewalOffer> offer = facts.RenewalOfferFor(unit, offerId);

        Assert.Equal(found, offer.HasValue);
    }

    // An offer is found only by the identifier the record's lookup uses: an offer that states only
    // a unit is not found by an offer id, and one that states only an offer id is not found by a unit.
    [Fact]
    public void RenewalOfferFor_AnOfferMissingTheIdentifierLookedUp_IsNotFound()
    {
        Assert.False(new PropertyFacts("Lakeview Commons", RenewalOffers: [new RenewalOffer(Unit: "A1")]).RenewalOfferFor(null, "REN-1").HasValue);
        Assert.False(new PropertyFacts("Lakeview Commons", RenewalOffers: [new RenewalOffer(OfferId: "REN-1")]).RenewalOfferFor("A1", null).HasValue);
    }

    // A property with no renewal offers stated has none to find.
    [Fact]
    public void RenewalOfferFor_APropertyWithNoOffers_FindsNothing()
    {
        Assert.False(new PropertyFacts("Lakeview Commons").RenewalOfferFor("A‑204", null).HasValue);
        Assert.False(new PropertyFacts("Lakeview Commons").RenewalOfferFor(null, "REN-1").HasValue);
    }

    // A unit is the same identifier whichever dash character wrote it, the same fold
    // PropertyLink.For already applies before it builds the unit's link segment: an offer
    // authored with a plain hyphen is still found by a record whose unit states a non-breaking
    // one, and the reverse.
    [Theory]
    [InlineData("A-204", "A‑204")]
    [InlineData("A‑204", "A-204")]
    public void RenewalOfferFor_UnitsWrittenWithDifferentDashCharacters_StillMatch(string offerUnit, string recordUnit)
    {
        var facts = new PropertyFacts("Lakeview Commons", RenewalOffers: [new RenewalOffer(Unit: offerUnit)]);

        Assert.True(facts.RenewalOfferFor(recordUnit, null).HasValue);
    }

    // The record and the property-data file are authored independently, the same reason
    // PropertyData.FactsFor matches a property name case-insensitively: a unit or offer id is the
    // same identifier whichever case it was typed with.
    [Theory]
    [InlineData("A-204", "a-204", null, null)]
    [InlineData("a-204", "A-204", null, null)]
    [InlineData(null, null, "REN-A204-2026", "ren-a204-2026")]
    [InlineData(null, null, "ren-a204-2026", "REN-A204-2026")]
    public void RenewalOfferFor_IdentifiersWrittenWithDifferentCase_StillMatch(string? offerUnit, string? recordUnit, string? offerId, string? recordOfferId)
    {
        var facts = new PropertyFacts("Lakeview Commons", RenewalOffers: [new RenewalOffer(Unit: offerUnit, OfferId: offerId)]);

        Assert.True(facts.RenewalOfferFor(recordUnit, recordOfferId).HasValue);
    }
}
