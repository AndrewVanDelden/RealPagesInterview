using Agent.Composition;
using Xunit;

namespace Agent.Tests.Composition;

// A tour option is written as month, zero-padded day, four-digit year and the local 12-hour time, the
// same in every language, so a reader in any locale knows which day it names and no day-month order
// can be read backwards.
public class TourSlotTextTests
{
    [Theory]
    [InlineData("2026-10-27T10:00:00-05:00", "Oct 27, 2026, 10:00 AM")]
    [InlineData("2025-12-05T14:30:00-06:00", "Dec 05, 2025, 2:30 PM")]
    [InlineData("2026-03-09T00:05:00Z", "Mar 09, 2026, 12:05 AM")]
    public void Write_ASlot_IsMonthDayYearAndLocalTime(string slot, string expected)
    {
        Assert.Equal(expected, TourSlotText.Write(DateTimeOffset.Parse(slot)));
    }

    // The scorer reads an option back to its date; text in any other shape is not a slot.
    [Theory]
    [InlineData("Oct 27, 2026, 10:00 AM", true, 2026, 10, 27)]
    [InlineData("Dec 05, 2025, 2:30 PM", true, 2025, 12, 5)]
    [InlineData("Thu", false, 0, 0, 0)]
    [InlineData("2026-10-27", false, 0, 0, 0)]
    public void TryReadDate_ReadsOnlyTheSlotFormat(string text, bool expectedRead, int year, int month, int day)
    {
        bool read = TourSlotText.TryReadDate(text, out DateOnly date);

        Assert.Equal(expectedRead, read);
        Assert.Equal(expectedRead ? new DateOnly(year, month, day) : default, date);
    }
}
