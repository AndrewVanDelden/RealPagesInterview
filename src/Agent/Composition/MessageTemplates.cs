using System.Collections.Frozen;

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
    FrozenDictionary<string, string> CtaPhraseByType,
    FrozenDictionary<string, IReadOnlyList<string>> SmsOptionsByCtaType,
    IReadOnlyList<string> GenericSmsOptions)
{
    // A9: a call to action this set has no phrase for is spelled from its own type, which
    // is the only wording the record supplies. That reads as English for a Spanish record,
    // and saying the type is more honest than inventing a translation of a value the
    // program has never seen.
    public string CtaPhrase(string ctaType) =>
        CtaPhraseByType.TryGetValue(ctaType, out string? phrase) ? phrase : ctaType.Replace('_', ' ');

    // A10: the options are prose, so their content is per language; which call to action
    // gets which options is this set's row, and anything unrecognized takes the generic
    // pair rather than going without a payload.
    public IReadOnlyList<string> SmsOptions(string ctaType) =>
        SmsOptionsByCtaType.TryGetValue(ctaType, out IReadOnlyList<string>? options) ? options : GenericSmsOptions;
}
