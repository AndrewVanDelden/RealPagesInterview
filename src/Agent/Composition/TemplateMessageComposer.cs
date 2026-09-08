using Agent.Common;
using Agent.Domain;

namespace Agent.Composition;

// The offline composer and the fallback (A18). Every fact is optional (D1): an absent
// first name means no name in the greeting, an absent property means no property fact,
// an absent primary_cta means the generic reply call to action (A9, A12). The opt-out
// phrase for the channel is always present.
public sealed class TemplateMessageComposer : IMessageComposer
{
    // priorViolations is ignored: this composer is deterministic, so retrying with the
    // same input can never produce a different result. ValidatingMessageComposer relies
    // on that (a bounded loop that only ever inspects the first attempt in practice) and
    // treats this composer as the always-clean, un-retried fallback.
    public Task<Result<NextMessage>> ComposeAsync(
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

        string greeting = firstName is null ? "Hi" : $"Hi {firstName}";
        string interestPhrase = BuildInterestPhrase(profile);
        string ctaPhrase = primaryCta is null ? "learn more" : primaryCta.Replace('_', ' ');
        string ctaType = primaryCta is null ? PrimaryCtaVocabulary.GenericCtaType : PrimaryCtaVocabulary.ToCtaType(primaryCta);

        string body = channel == CommunicationChannel.Email
            ? $"{greeting},\n{interestPhrase}Reply or click to {ctaPhrase}{(propertyName is null ? string.Empty : $" at {propertyName}")}.\nTo opt out of emails, reply STOP."
            : $"{greeting}!{(propertyName is null ? string.Empty : $" Welcome to {propertyName}.")} {interestPhrase}Reply to {ctaPhrase}. Reply STOP to opt out.";

        string? subject = channel == CommunicationChannel.Email
            ? propertyName is null ? "Your next step" : $"Tour {propertyName}"
            : null;

        var message = new NextMessage(channel, null, subject, body, new Cta(ctaType, null, null));

        return Task.FromResult(Result<NextMessage>.Success(message));
    }

    private static string? Present(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

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
