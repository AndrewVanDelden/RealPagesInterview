using System.Collections.Frozen;
using Agent.Composition;

namespace Agent.Evaluation;

// How the scorer reads one sms reply option. A weekday offered as an abbreviation asks the recipient
// the same question as the day's full name, so a weekday's own abbreviation and full name fold to one
// spelling before two options are compared. English and Spanish fold to different canonical spellings
// for the same weekday, never to one shared spelling: an English option is a fact about which language
// the message was sent in, not an interchangeable rendering of a Spanish label's weekday, so the fold
// must not let one pass for the other. The product offers dated tour slots while the labels name
// weekdays, so an option written as a tour slot is compared by date: a label's weekday means the first
// date with that weekday after the label's own send date, and a date carries no language. Anything
// else is compared as written, trimmed and without regard to case. The scorer's rule only.
internal static class ReplyOptionSpelling
{
    private static readonly FrozenDictionary<string, (string Key, DayOfWeek Day)> WeekdaysBySpelling =
        new Dictionary<string, (string Key, DayOfWeek Day)>(StringComparer.OrdinalIgnoreCase)
        {
            ["mon"] = ("en:mon", DayOfWeek.Monday), ["monday"] = ("en:mon", DayOfWeek.Monday), ["lun"] = ("es:mon", DayOfWeek.Monday), ["lunes"] = ("es:mon", DayOfWeek.Monday),
            ["tue"] = ("en:tue", DayOfWeek.Tuesday), ["tues"] = ("en:tue", DayOfWeek.Tuesday), ["tuesday"] = ("en:tue", DayOfWeek.Tuesday), ["mar"] = ("es:tue", DayOfWeek.Tuesday), ["martes"] = ("es:tue", DayOfWeek.Tuesday),
            ["wed"] = ("en:wed", DayOfWeek.Wednesday), ["weds"] = ("en:wed", DayOfWeek.Wednesday), ["wednesday"] = ("en:wed", DayOfWeek.Wednesday), ["mié"] = ("es:wed", DayOfWeek.Wednesday), ["mie"] = ("es:wed", DayOfWeek.Wednesday), ["miércoles"] = ("es:wed", DayOfWeek.Wednesday), ["miercoles"] = ("es:wed", DayOfWeek.Wednesday),
            ["thu"] = ("en:thu", DayOfWeek.Thursday), ["thur"] = ("en:thu", DayOfWeek.Thursday), ["thurs"] = ("en:thu", DayOfWeek.Thursday), ["thursday"] = ("en:thu", DayOfWeek.Thursday), ["jue"] = ("es:thu", DayOfWeek.Thursday), ["jueves"] = ("es:thu", DayOfWeek.Thursday),
            ["fri"] = ("en:fri", DayOfWeek.Friday), ["friday"] = ("en:fri", DayOfWeek.Friday), ["vie"] = ("es:fri", DayOfWeek.Friday), ["viernes"] = ("es:fri", DayOfWeek.Friday),
            ["sat"] = ("en:sat", DayOfWeek.Saturday), ["saturday"] = ("en:sat", DayOfWeek.Saturday), ["sáb"] = ("es:sat", DayOfWeek.Saturday), ["sab"] = ("es:sat", DayOfWeek.Saturday), ["sábado"] = ("es:sat", DayOfWeek.Saturday), ["sabado"] = ("es:sat", DayOfWeek.Saturday),
            ["sun"] = ("en:sun", DayOfWeek.Sunday), ["sunday"] = ("en:sun", DayOfWeek.Sunday), ["dom"] = ("es:sun", DayOfWeek.Sunday), ["domingo"] = ("es:sun", DayOfWeek.Sunday),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // O(n) in the options' length: a trim, a trailing period dropped, at most one parse and one lookup
    // each. labelSendDate is the label's own local send date, null when the label states none, and a
    // weekday with no date to count from is compared as text.
    public static bool SameOption(string option, string expectedOption, DateOnly? labelSendDate)
    {
        if (labelSendDate is { } sendDate
            && TourSlotText.TryReadDate(option, out DateOnly offeredDate)
            && WeekdaysBySpelling.TryGetValue(Trimmed(expectedOption), out (string Key, DayOfWeek Day) labelWeekday))
        {
            return offeredDate == FirstDateAfter(sendDate, labelWeekday.Day);
        }

        return string.Equals(Fold(option), Fold(expectedOption), StringComparison.OrdinalIgnoreCase);
    }

    private static string Trimmed(string option) => option.Trim().TrimEnd('.');

    private static string Fold(string option)
    {
        string trimmed = Trimmed(option);

        return WeekdaysBySpelling.TryGetValue(trimmed, out (string Key, DayOfWeek Day) weekday) ? weekday.Key : trimmed;
    }

    // The first date strictly after date that falls on day: one to seven days later.
    private static DateOnly FirstDateAfter(DateOnly date, DayOfWeek day) =>
        date.AddDays((((int)day - (int)date.DayOfWeek + 6) % 7) + 1);
}
