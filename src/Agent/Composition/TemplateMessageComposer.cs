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
        string interestPhrase = BuildInterestPhrase(prospectCase.Input.Profile);
        string ctaPhrase = primaryCta.Replace('_', ' ');

        string body = channel == CommunicationChannel.Email
            ? $"Hi {firstName},\n{interestPhrase}Reply or click to {ctaPhrase} at {propertyName}.\nTo opt out of emails, reply STOP."
            : $"Hi {firstName}! Welcome to {propertyName}. {interestPhrase}Reply to {ctaPhrase}. Reply STOP to opt out.";

        string? subject = channel == CommunicationChannel.Email
            ? $"Tour {propertyName}"
            : null;

        var cta = new Cta(primaryCta, null, null);
        var message = new NextMessage(channel, null, subject, body, cta);

        return Task.FromResult(Result<NextMessage>.Success(message));
    }

    private static string BuildInterestPhrase(ProspectProfile profile)
    {
        if (profile.AmenityInterest is { Count: > 0 } amenities)
        {
            return $"We heard you're interested in {string.Join(" and ", amenities)}. ";
        }

        if (profile.CityInterest is { Length: > 0 } city)
        {
            return $"We heard you're looking in {city}. ";
        }

        return string.Empty;
    }
}
