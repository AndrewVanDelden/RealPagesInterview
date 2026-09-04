using Agent.Common;
using Agent.Composition;
using Agent.Domain;
using Agent.Safety;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Safety;

public class ValidatingMessageComposerTests
{
    private static readonly ISafetyValidator Validator = new SafetyValidator();
    private static readonly IMessageComposer FallbackComposer = new TemplateMessageComposer();

    private static NextMessage CleanMessage() =>
        new(CommunicationChannel.Sms, null, null, "Hi Taylor! Book a tour. Reply STOP to opt out.", null);

    private static NextMessage BadMessage() =>
        new(CommunicationChannel.Sms, null, null, "This community is families only.", null);

    [Fact]
    public async Task ComposeAsync_FirstAttemptClean_ReturnsFirstAttemptWithoutRetry()
    {
        NextMessage cleanMessage = CleanMessage();
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(cleanMessage));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<NextMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.Same(cleanMessage, result.Value);
        Assert.Equal(1, innerComposer.CallCount);
    }

    [Fact]
    public async Task ComposeAsync_FirstAttemptBadSecondAttemptClean_ReturnsCorrectedSecondAttempt()
    {
        NextMessage cleanMessage = CleanMessage();
        var innerComposer = new SequenceMessageComposer(
            Result<NextMessage>.Success(BadMessage()),
            Result<NextMessage>.Success(cleanMessage));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<NextMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.Same(cleanMessage, result.Value);
        Assert.Equal(2, innerComposer.CallCount);
    }

    [Fact]
    public async Task ComposeAsync_BothAttemptsBad_FallsBackToSafeComposer()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<NextMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        SafetyValidationResult finalValidation = Validator.Validate(result.Value!, prospectCase.Assertions.Constraints);
        Assert.Empty(finalValidation.Violations);
    }

    [Fact]
    public async Task ComposeAsync_ComposerKeepsFailing_FallsBackToSafeComposer()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Failure("boom"));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<NextMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, innerComposer.CallCount);
    }

    [Fact]
    public async Task ComposeAsync_LoopIsBounded_NeverCallsInnerComposerMoreThanTwice()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Equal(2, innerComposer.CallCount);
    }

    [Fact]
    public async Task ComposeAsync_FallbackAlsoUnsafe_ReturnsFailureRatherThanUnvalidatedMessage()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()));
        var unsafeFallback = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, unsafeFallback);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<NextMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ComposeAsync_FallbackComposerFailsToCompose_ReturnsFailure()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()));
        var failingFallback = new SequenceMessageComposer(Result<NextMessage>.Failure("fallback boom"));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, failingFallback);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<NextMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ComposeAsync_RetryAttempt_ReceivesPriorViolationsFromFirstAttempt()
    {
        var innerComposer = new SequenceMessageComposer(
            Result<NextMessage>.Success(BadMessage()),
            Result<NextMessage>.Success(CleanMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.NotNull(innerComposer.LastPriorViolations);
        Assert.NotEmpty(innerComposer.LastPriorViolations);
    }

    // A Result.Failure from the inner composer (e.g. a wrong cta_type, a malformed
    // completion) is not a safety violation, but it's still something the retry should
    // know about - otherwise the second attempt repeats the exact same prompt with zero
    // corrective signal, wasting the one retry this loop has.
    [Fact]
    public async Task ComposeAsync_FirstAttemptFails_RetryReceivesFailureReasonAsCorrection()
    {
        var innerComposer = new SequenceMessageComposer(
            Result<NextMessage>.Failure("Model returned cta_type 'call_now' but 'schedule_tour' was required."),
            Result<NextMessage>.Success(CleanMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.NotNull(innerComposer.LastPriorViolations);
        Assert.Contains("Model returned cta_type 'call_now' but 'schedule_tour' was required.", innerComposer.LastPriorViolations);
    }

    [Fact]
    public async Task ComposeAsync_FirstAttempt_ReceivesNoPriorViolations()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(CleanMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Null(innerComposer.LastPriorViolations);
    }
}
