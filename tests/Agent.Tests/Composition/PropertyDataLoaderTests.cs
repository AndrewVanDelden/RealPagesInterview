using Agent.Common;
using Agent.Composition;
using Xunit;

namespace Agent.Tests.Composition;

// The property data file stands in for the property management system's feed. It comes from
// outside the program, so it is read whole and checked before any record runs, every bad
// property is named by its 1-based place and its name in one failure, and a member the format
// does not define is refused rather than skipped.
public class PropertyDataLoaderTests
{
    private const string NotInFormat =
        "does not match the property format (a value of the wrong type, a required member missing, or a member the format does not define).";

    private static Result<PropertyData> Load(string text) => PropertyDataLoader.Load(new StringReader(text));

    [Fact]
    public void Load_AValidFile_ReadsEveryFact()
    {
        Result<PropertyData> result = Load("""
            {
              "properties": [
                {
                  "property_name": "Oak Ridge Apartments",
                  "tour_availability": "Tours are available this week.",
                  "extended_tour_hours": "Evening and weekend tours are available.",
                  "starting_prices": [ { "floor_plan": "studio", "total_monthly_price": 1650, "as_of": "2025-12-08" } ],
                  "renewal_offers": [ { "unit": "A-204", "offer_id": "REN-1", "price_hold_days": 10, "text_reminders_offered": true }, { "offer_id": "REN-2" } ]
                },
                { "property_name": "Lakeview Commons" }
              ]
            }
            """);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Error);
        Assert.Equal(2, result.Value.Properties.Count);
        PropertyFacts facts = result.Value.Properties[0];
        Assert.Equal("Oak Ridge Apartments", facts.PropertyName);
        Assert.Equal("Tours are available this week.", facts.TourAvailability);
        Assert.Equal("Evening and weekend tours are available.", facts.ExtendedTourHours);
        Assert.Equal(new StartingPrice("studio", 1650m, new DateOnly(2025, 12, 8)), Assert.Single(facts.StartingPrices!));
        Assert.Equal([new RenewalOffer("A-204", "REN-1", 10, true), new RenewalOffer(OfferId: "REN-2")], facts.RenewalOffers!);
        Assert.Null(result.Value.Properties[1].StartingPrices);
    }

    [Fact]
    public void Load_BadProperties_NamesEveryOneByPositionAndName()
    {
        Result<PropertyData> result = Load("""
            {
              "properties": [
                { "property_name": " " },
                {
                  "property_name": "Oak Ridge",
                  "starting_prices": [ { "floor_plan": "studio", "total_monthly_price": 0, "as_of": "2025-12-08" }, null ],
                  "renewal_offers": [ { "price_hold_days": 10 }, { "unit": "A1", "price_hold_days": 0 }, null ]
                },
                { "property_name": "oak ridge" },
                { "property_name": "Lakeview", "colour": "red" },
                5
              ]
            }
            """);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            [
                "Property 1: property_name is blank.",
                "Property 2 (oak ridge): starting price 1 total_monthly_price 0 must be positive.",
                "Property 2 (oak ridge): starting price 2 is null.",
                "Property 2 (oak ridge): renewal offer 1 names neither a unit nor an offer_id.",
                "Property 2 (oak ridge): renewal offer 2 price_hold_days 0 must be positive.",
                "Property 2 (oak ridge): renewal offer 3 is null.",
                "Property 3 (oak ridge): duplicates property 2.",
                $"Property 4 (lakeview): {NotInFormat}",
                $"Property 5: {NotInFormat}",
            ],
            result.Error.Split(Environment.NewLine));
    }

    // A property's tour calendar is its weekdays, its tour time and its closed dates, each optional; a
    // property that states none has no calendar, and the tour slot planner's defaults apply to it.
    [Fact]
    public void Load_TourCalendar_ReadsItsDaysTimeAndClosedDates()
    {
        Result<PropertyData> result = Load("""
            {
              "properties": [
                {
                  "property_name": "Oak Ridge Apartments",
                  "tour_days": [ "tuesday", "Thursday" ],
                  "tour_time": "14:00",
                  "tour_closed_dates": [ "2025-12-25" ]
                },
                { "property_name": "Lakeview Commons", "tour_time": "09:30" },
                { "property_name": "Maple Grove" },
                { "property_name": "Aspen Ridge", "tour_closed_dates": [ "2026-01-01" ] }
              ]
            }
            """);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Error);
        TourCalendar calendar = result.Value.Properties[0].TourCalendar!;
        Assert.Equal([DayOfWeek.Tuesday, DayOfWeek.Thursday], calendar.OpenDays!);
        Assert.Equal(new TimeOnly(14, 0), calendar.TourTime);
        Assert.Equal([new DateOnly(2025, 12, 25)], calendar.ClosedDates!);
        Assert.Equal(new TourCalendar(TourTime: new TimeOnly(9, 30)), result.Value.Properties[1].TourCalendar);
        Assert.Null(result.Value.Properties[2].TourCalendar);
        TourCalendar closuresOnly = result.Value.Properties[3].TourCalendar!;
        Assert.Null(closuresOnly.OpenDays);
        Assert.Null(closuresOnly.TourTime);
        Assert.Equal([new DateOnly(2026, 1, 1)], closuresOnly.ClosedDates!);
    }

    // A weekday that is not a weekday's name, an empty list of days, a time not written as 24-hour
    // hours and minutes, and a null closed date are each named, so a calendar cannot silently offer
    // no slot or the wrong one.
    [Fact]
    public void Load_BadTourCalendar_NamesEveryFault()
    {
        Result<PropertyData> result = Load("""
            {
              "properties": [
                { "property_name": "Oak Ridge", "tour_days": [ "Funday", "1", null ], "tour_time": "2pm", "tour_closed_dates": [ null ] },
                { "property_name": "Lakeview", "tour_days": [] }
              ]
            }
            """);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            [
                "Property 1 (oak ridge): tour day 1 is not a weekday name.",
                "Property 1 (oak ridge): tour day 2 is not a weekday name.",
                "Property 1 (oak ridge): tour day 3 is not a weekday name.",
                "Property 1 (oak ridge): tour_time must be 24-hour HH:mm.",
                "Property 1 (oak ridge): tour closed date 1 is null.",
                "Property 2 (lakeview): tour_days is empty, so no tour could be offered.",
            ],
            result.Error.Split(Environment.NewLine));
    }

    // The parser's message quotes the file's own text, so the failure carries the exception type
    // and the position only.
    [Theory]
    [InlineData("{oops")]
    [InlineData("{}")]
    public void Load_NotAPropertyDataObject_FailsNamingThePositionOnly(string text)
    {
        Result<PropertyData> result = Load(text);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("Property data file failed to parse: JsonException", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("oops", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_Null_Fails()
    {
        Result<PropertyData> result = Load("null");

        Assert.False(result.IsSuccess);
        Assert.Equal("Property data file holds null, not a property data object.", result.Error);
    }
}
