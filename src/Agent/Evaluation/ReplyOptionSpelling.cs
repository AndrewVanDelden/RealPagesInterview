using System.Collections.Frozen;

namespace Agent.Evaluation;

// How the scorer reads one sms reply option: a weekday offered as an abbreviation asks the
// recipient the same question as the day's full name, so English and Spanish weekday names and
// their standard abbreviations fold to one spelling before two options are compared. Anything
// else is compared as written, trimmed and without regard to case. The scorer's rule only: the
// product sends its own language set's spelling.
internal static class ReplyOptionSpelling
{
    private static readonly FrozenDictionary<string, string> WeekdaysBySpelling =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mon"] = "mon", ["monday"] = "mon", ["lun"] = "mon", ["lunes"] = "mon",
            ["tue"] = "tue", ["tues"] = "tue", ["tuesday"] = "tue", ["mar"] = "tue", ["martes"] = "tue",
            ["wed"] = "wed", ["weds"] = "wed", ["wednesday"] = "wed", ["mié"] = "wed", ["mie"] = "wed", ["miércoles"] = "wed", ["miercoles"] = "wed",
            ["thu"] = "thu", ["thur"] = "thu", ["thurs"] = "thu", ["thursday"] = "thu", ["jue"] = "thu", ["jueves"] = "thu",
            ["fri"] = "fri", ["friday"] = "fri", ["vie"] = "fri", ["viernes"] = "fri",
            ["sat"] = "sat", ["saturday"] = "sat", ["sáb"] = "sat", ["sab"] = "sat", ["sábado"] = "sat", ["sabado"] = "sat",
            ["sun"] = "sun", ["sunday"] = "sun", ["dom"] = "sun", ["domingo"] = "sun",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // O(n) in the option's length: a trim, a trailing period dropped, one lookup.
    public static bool SameOption(string option, string expectedOption) =>
        string.Equals(Fold(option), Fold(expectedOption), StringComparison.OrdinalIgnoreCase);

    private static string Fold(string option)
    {
        string trimmed = option.Trim().TrimEnd('.');

        return WeekdaysBySpelling.TryGetValue(trimmed, out string? weekday) ? weekday : trimmed;
    }
}
