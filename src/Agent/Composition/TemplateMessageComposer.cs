using Agent.Common;
using Agent.Domain;

namespace Agent.Composition;

// The offline composer and the fallback (A18). Every fact is optional (D1): an absent
// first name means no name in the greeting, an absent property means no property fact and
// no link, an absent primary_cta means the generic reply call to action (A9, A12). The
// opt-out phrase for the channel is always present, and so is the channel's call-to-action
// payload: sms enumerates the catalog's reply options, email carries the link (A10, D25).
public sealed class TemplateMessageComposer : IMessageComposer
{
    // priorViolations is ignored: this composer is deterministic, so retrying with the
    // same input can never produce a different result. ValidatingMessageComposer relies
    // on that (a bounded loop that only ever inspects the first attempt in practice) and
    // treats this composer as the always-clean, un-retried fallback.
    public Task<Result<ComposedMessage>> ComposeAsync(
        ProspectCase prospectCase,
        CommunicationChannel channel,
        IReadOnlyList<string>? priorViolations = null,
        CancellationToken cancellationToken = default)
    {
        ProspectContext context = prospectCase.ContextOrEmpty;
        ProspectProfile profile = context.ProfileOrEmpty;
        string? firstName = Present(profile.FirstName);
        string? propertyName = Present(context.PropertyName);
        string? primaryCta = Present(prospectCase.ConstraintsOrEmpty.PrimaryCta);
        CallToAction callToAction = CallToActionCatalog.Resolve(primaryCta);

        string greeting = firstName is null ? "Hi" : $"Hi {firstName}";
        string interestPhrase = BuildInterestPhrase(profile);
        string ctaPhrase = primaryCta is null ? "learn more" : primaryCta.Replace('_', ' ');

        // A10: the payload shape is the channel's rule, so an sms always enumerates options
        // and an email always carries a link when the record states a property to build one
        // from. Only the content comes from the catalog.
        bool isEmail = channel == CommunicationChannel.Email;
        Uri? link = isEmail ? PropertyLink.For(propertyName, callToAction.LinkPath) : null;
        IReadOnlyList<string>? options = isEmail ? null : callToAction.SmsOptions;

        string body = isEmail
            ? $"{greeting},\n{interestPhrase}Reply or click to {ctaPhrase}{(propertyName is null ? string.Empty : $" at {propertyName}")}.\n{(link is null ? string.Empty : $"Get started: {link}\n")}To opt out of emails, reply STOP."
            : $"{greeting}!{(propertyName is null ? string.Empty : $" Welcome to {propertyName}.")} {interestPhrase}Reply to {ctaPhrase}. {NumberedOptions(callToAction.SmsOptions)} Reply STOP to opt out.";

        string? subject = isEmail
            ? propertyName is null ? "Your next step" : $"Tour {propertyName}"
            : null;

        var message = new NextMessage(channel, null, subject, body, new Cta(callToAction.Type, options, link));
        var composed = new ComposedMessage(message, new CompositionNotes(ComposerNames.Template, Attempts: 1));

        return Task.FromResult(Result<ComposedMessage>.Success(composed));
    }

    private static string? Present(string? value) => Presence.IsAbsent(value) ? null : value;

    // A10: sample 1's own spelling, "Reply 1 for Thu, 2 for Fri.", so the body and
    // cta.options carry the same list and a reader of the message can act on it.
    // O(n) in the number of options, which is the catalog's row, not input.
    private static string NumberedOptions(IReadOnlyList<string> options) =>
        $"Reply {string.Join(", ", options.Select((option, index) => $"{index + 1} for {option}"))}.";

    private static string BuildInterestPhrase(ProspectProfile profile)
    {
        var clauses = new List<string>();

        if (profile.Amenities.Count > 0)
        {
            clauses.Add($"interested in {string.Join(" and ", profile.Amenities)}");
        }

        if (profile.City.Length > 0)
        {
            clauses.Add($"looking in {profile.City}");
        }

        return clauses.Count > 0
            ? $"We heard you're {string.Join(" and ", clauses)}. "
            : string.Empty;
    }
}
