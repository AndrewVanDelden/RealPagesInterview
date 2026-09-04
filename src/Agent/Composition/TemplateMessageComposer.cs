using Agent.Common;
using Agent.Domain;

namespace Agent.Composition;

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
        string firstName = prospectCase.Input.Profile.FirstName;
        string propertyName = prospectCase.Input.PropertyName;
        string primaryCta = prospectCase.Assertions.Constraints.PrimaryCta;

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(primaryCta))
        {
            return Task.FromResult(Result<NextMessage>.Failure(
                "Prospect case is missing a required field (first name, property name, or primary CTA)."));
        }

        string interestPhrase = BuildInterestPhrase(prospectCase.Input.Profile);
        string ctaPhrase = primaryCta.Replace('_', ' ');

        string body = channel == CommunicationChannel.Email
            ? $"Hi {firstName},\n{interestPhrase}Reply or click to {ctaPhrase} at {propertyName}.\nTo opt out of emails, reply STOP."
            : $"Hi {firstName}! Welcome to {propertyName}. {interestPhrase}Reply to {ctaPhrase}. Reply STOP to opt out.";

        string? subject = channel == CommunicationChannel.Email
            ? $"Tour {propertyName}"
            : null;

        var cta = new Cta(PrimaryCtaVocabulary.ToCtaType(primaryCta), null, null);
        var message = new NextMessage(channel, null, subject, body, cta);

        return Task.FromResult(Result<NextMessage>.Success(message));
    }

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
