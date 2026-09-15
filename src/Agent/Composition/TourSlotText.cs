using System.Globalization;

namespace Agent.Composition;

// A tour slot as a reply option: month, zero-padded day, four-digit year and the local 12-hour time,
// written in the invariant culture for every language. A date spelled with its month named cannot be
// read day-first or month-first by mistake, so a recipient in any locale knows which day it is.
public static class TourSlotText
{
    private const string Format = "MMM dd, yyyy, h:mm tt";

    // O(1): one fixed-width format.
    public static string Write(DateTimeOffset slot) => slot.ToString(Format, CultureInfo.InvariantCulture);

    // The reply options a call to action takes from the planned tour slots: a tour invitation's are the
    // slots, each written as a date; any other call to action, or a tour invitation with no slot
    // planned, takes none from here. O(s) in the slots.
    public static IReadOnlyList<string>? OptionsFor(string ctaType, IReadOnlyList<DateTimeOffset>? tourSlots) =>
        ctaType == CallToActionCatalog.TourType && tourSlots is { Count: > 0 } slots ? [.. slots.Select(Write)] : null;

    // The date an option written by Write names, for a reader that compares options by day. Text in any
    // other shape is not a slot. O(n) in the text's length.
    public static bool TryReadDate(string text, out DateOnly date)
    {
        bool read = DateTime.TryParseExact(text.Trim(), Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed);
        date = read ? DateOnly.FromDateTime(parsed) : default;
        return read;
    }
}
