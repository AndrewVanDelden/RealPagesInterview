using System.Collections.Frozen;

namespace Agent.Evaluation;

// How the scorer reads one sms reply option: a weekday offered as an abbreviation asks the
// recipient the same question as the day's full name, so a weekday's own abbreviation and full
// name fold to one spelling before two options are compared. English and Spanish fold to
// different canonical spellings for the same weekday, never to one shared spelling: an English
// option is a fact about which language the message was sent in, not an interchangeable rendering
// of a Spanish label's weekday, so the fold must not let one pass for the other. Anything else is
// compared as written, trimmed and without regard to case. The scorer's rule only: the product
// sends its own language set's spelling.
internal static class ReplyOptionSpelling
{
    private static readonly FrozenDictionary<string, string> WeekdaysBySpelling =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mon"] = "en:mon", ["monday"] = "en:mon", ["lun"] = "es:mon", ["lunes"] = "es:mon",
            ["tue"] = "en:tue", ["tues"] = "en:tue", ["tuesday"] = "en:tue", ["mar"] = "es:tue", ["martes"] = "es:tue",
            ["wed"] = "en:wed", ["weds"] = "en:wed", ["wednesday"] = "en:wed", ["mié"] = "es:wed", ["mie"] = "es:wed", ["miércoles"] = "es:wed", ["miercoles"] = "es:wed",
            ["thu"] = "en:thu", ["thur"] = "en:thu", ["thurs"] = "en:thu", ["thursday"] = "en:thu", ["jue"] = "es:thu", ["jueves"] = "es:thu",
            ["fri"] = "en:fri", ["friday"] = "en:fri", ["vie"] = "es:fri", ["viernes"] = "es:fri",
            ["sat"] = "en:sat", ["saturday"] = "en:sat", ["sáb"] = "es:sat", ["sab"] = "es:sat", ["sábado"] = "es:sat", ["sabado"] = "es:sat",
            ["sun"] = "en:sun", ["sunday"] = "en:sun", ["dom"] = "es:sun", ["domingo"] = "es:sun",
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
