using Agent.Common;
using Agent.Domain;

namespace Agent.Composition;

public sealed class TemplateMessageComposer : IMessageComposer
{
    public Task<Result<NextMessage>> ComposeAsync(ProspectCase prospectCase, CommunicationChannel channel, CancellationToken cancellationToken = default)
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

        if (profile.HasAmenityInterest)
        {
            clauses.Add($"interested in {string.Join(" and ", profile.AmenityInterest!)}");
        }

        if (profile.HasCityInterest)
        {
            clauses.Add($"looking in {profile.CityInterest}");
        }

        return clauses.Count > 0
            ? $"We heard you're {string.Join(" and ", clauses)}. "
            : string.Empty;
    }
}
