using System.Collections.Frozen;
using System.Globalization;

namespace Agent.Composition;

// One language's worth of the offline composer's prose (A13). Every string a human
// reads lives in a set like this one, so adding a language is one file plus one row in
// MessageTemplateCatalog, and the sentence order stays in TemplateMessageComposer where
// both languages share it. The call-to-action vocabulary itself stays language-neutral in
// CallToActionCatalog: which call to action exists and where its link points is not prose.
internal sealed record MessageTemplates(
    string GreetingWithName,
    string GreetingWithoutName,
    string SmsWelcome,
    string InterestAmenities,
    string InterestCity,
    string InterestSentence,
    string Conjunction,
    string SmsCtaSentence,
    string SmsOptionsSentence,
    string SmsOption,
    string SmsOptOut,
    string EmailCtaSentence,
    string EmailAtProperty,
    string EmailLinkLine,
    string EmailOptOut,
    string EmailSubjectForProperty,
    string EmailSubjectGeneric,
    string RenewalPriceHoldSentence,
    string RenewalTextRemindersSentence,
    FrozenDictionary<string, string> CtaPhraseByType,
    FrozenDictionary<string, IReadOnlyList<string>> SmsOptionsByCtaType,
    IReadOnlyList<string> GenericSmsOptions,
    IReadOnlyList<string> GenericSmsOptionsWithoutTour)
{
    // A9: a call to action this set has no phrase for is spelled from its own type, which
    // is the only wording the record supplies. That reads as English for a Spanish record,
    // and saying the type is more honest than inventing a translation of a value the
    // program has never seen.
    public string CtaPhrase(string ctaType) =>
        CtaPhraseByType.TryGetValue(ctaType, out string? phrase) ? phrase : ctaType.Replace('_', ' ');

    // A10: the options are prose, so their content is per language; which call to action
    // gets which options is this set's row, and anything unrecognized takes the generic
    // list rather than going without a payload. The generic list offers a tour, which only a
    // prospect is shopping for, so a record that is not a prospect gets the list without it.
    // A10: sample 1's shape, "Reply 1 for ..., 2 for ...", the one sentence both composers write so the
    // body and cta.options always carry the same list. The numbered options are joined with a
    // semicolon because a dated tour option carries commas of its own. O(n) in the number of options,
    // a language set's row, the planned slots or the model's list, never input.
    public string NumberedOptionsSentence(IReadOnlyList<string> options) =>
        string.Format(
            CultureInfo.InvariantCulture,
            SmsOptionsSentence,
            string.Join("; ", options.Select((option, index) => string.Format(CultureInfo.InvariantCulture, SmsOption, index + 1, option))));

    public IReadOnlyList<string> SmsOptions(string ctaType, bool forProspect) =>
        SmsOptionsByCtaType.TryGetValue(ctaType, out IReadOnlyList<string>? options)
            ? options
            : forProspect ? GenericSmsOptions : GenericSmsOptionsWithoutTour;
}
