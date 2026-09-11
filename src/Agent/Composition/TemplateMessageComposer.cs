using System.Globalization;
using Agent.Common;
using Agent.Domain;

namespace Agent.Composition;

// The offline composer and the fallback (A18). Every fact is optional in the input: an absent
// first name means no name in the greeting, an absent property means no property fact and
// no link, an absent primary_cta means the generic reply call to action (A9, A12). The
// opt-out phrase is always present, and so is the channel's call-to-action payload: sms
// enumerates the options, email carries the link (A10). The prose comes from the
// record's language set (A13); the sentence order lives here because every set shares
// it, and a language with no set is served in English with LocaleApplied false.
public sealed class TemplateMessageComposer : IMessageComposer
{
    // priorViolations is ignored: this composer is deterministic, so retrying with the
    // same input can never produce a different result. ValidatingMessageComposer relies
    // on that (a bounded loop that only ever inspects the first attempt in practice) and
    // treats this composer as the always-clean, un-retried fallback.
    public Task<ComposeOutcome> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default)
    {
        ProspectContext context = prospectCase.ContextOrEmpty;
        ProspectProfile profile = context.ProfileOrEmpty;
        string? firstName = Present(profile.FirstName);
        string? propertyName = Present(context.PropertyName);
        CallToAction callToAction = CallToActionCatalog.Resolve(
            prospectCase.ConstraintsOrEmpty.PrimaryCta,
            prospectCase.Persona,
            prospectCase.LifecycleStage);
        (MessageTemplates templates, bool localeApplied) = MessageTemplateCatalog.Resolve(context.Language);

        string greeting = firstName is null ? templates.GreetingWithoutName : Fill(templates.GreetingWithName, firstName);
        string interestPhrase = BuildInterestPhrase(profile, templates);
        string ctaPhrase = templates.CtaPhrase(callToAction.Type);

        // A10: the payload shape is the channel's rule, so an sms always enumerates options
        // and an email always carries a link when the record states a property to build one
        // from. Only the content comes from the catalog and the language set.
        bool isEmail = channel == CommunicationChannel.Email;
        Uri? link = isEmail ? PropertyLink.For(propertyName, callToAction.LinkPath) : null;
        IReadOnlyList<string> optionTexts = templates.SmsOptions(callToAction.Type);

        string body = isEmail
            ? EmailBody(templates, greeting, interestPhrase, ctaPhrase, propertyName, link)
            : SmsBody(templates, greeting, interestPhrase, ctaPhrase, propertyName, optionTexts);

        string? subject = isEmail
            ? propertyName is null ? templates.EmailSubjectGeneric : Fill(templates.EmailSubjectForProperty, propertyName)
            : null;

        var message = new NextMessage(channel, null, subject, body, new Cta(callToAction.Type, isEmail ? null : optionTexts, link));
        var composed = new ComposedMessage(message, CompositionNotes.ForComposer(ComposerNames.Template, localeApplied));

        return Task.FromResult<ComposeOutcome>(new ComposeOutcome.Composed(composed));
    }

    private static string SmsBody(
        MessageTemplates templates,
        string greeting,
        string interestPhrase,
        string ctaPhrase,
        string? propertyName,
        IReadOnlyList<string> optionTexts)
    {
        string welcome = propertyName is null ? string.Empty : Fill(templates.SmsWelcome, propertyName);
        string options = Fill(templates.SmsOptionsSentence, NumberedOptions(templates, optionTexts));

        return $"{greeting}!{welcome} {interestPhrase}{Fill(templates.SmsCtaSentence, ctaPhrase)} {options} {templates.SmsOptOut}";
    }

    private static string EmailBody(
        MessageTemplates templates,
        string greeting,
        string interestPhrase,
        string ctaPhrase,
        string? propertyName,
        Uri? link)
    {
        string atProperty = propertyName is null ? string.Empty : Fill(templates.EmailAtProperty, propertyName);
        string linkLine = link is null ? string.Empty : $"{Fill(templates.EmailLinkLine, link.ToString())}\n";

        return $"{greeting},\n{interestPhrase}{Fill(templates.EmailCtaSentence, ctaPhrase)}{atProperty}.\n{linkLine}{templates.EmailOptOut}";
    }

    private static string? Present(string? value) => Presence.IsAbsent(value) ? null : value;

    private static string Fill(string template, params object[] values) =>
        string.Format(CultureInfo.InvariantCulture, template, values);

    // A10: sample 1's own spelling, "Reply 1 for Thu, 2 for Fri.", so the body and
    // cta.options carry the same list and a reader of the message can act on it.
    // O(n) in the number of options, which is the language set's row, not input.
    private static string NumberedOptions(MessageTemplates templates, IReadOnlyList<string> optionTexts) =>
        string.Join(", ", optionTexts.Select((option, index) => Fill(templates.SmsOption, index + 1, option)));

    private static string BuildInterestPhrase(ProspectProfile profile, MessageTemplates templates)
    {
        var clauses = new List<string>();

        if (profile.Amenities.Count > 0)
        {
            clauses.Add(Fill(templates.InterestAmenities, string.Join(templates.Conjunction, profile.Amenities)));
        }

        if (profile.City.Length > 0)
        {
            clauses.Add(Fill(templates.InterestCity, profile.City));
        }

        return clauses.Count > 0
            ? Fill(templates.InterestSentence, string.Join(templates.Conjunction, clauses))
            : string.Empty;
    }
}
