using Agent.Common;
using Agent.Composition;
using Agent.Domain;
using Agent.Safety;
using Agent.Tests.TestSupport;
using Microsoft.Extensions.Logging;
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

        Result<ComposedMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.Same(cleanMessage, result.Value.Message);
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

        Result<ComposedMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.Same(cleanMessage, result.Value.Message);
        Assert.Equal(2, innerComposer.CallCount);
    }

    [Fact]
    public async Task ComposeAsync_BothAttemptsBad_FallsBackToSafeComposer()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<ComposedMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        SafetyValidationResult finalValidation = Validator.Validate(result.Value!.Message, prospectCase.ConstraintsOrEmpty);
        Assert.Empty(finalValidation.Violations);
    }

    [Fact]
    public async Task ComposeAsync_ComposerKeepsFailing_FallsBackToSafeComposer()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Failure("boom"));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<ComposedMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

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

        Result<ComposedMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ComposeAsync_FallbackComposerFailsToCompose_ReturnsFailure()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()));
        var failingFallback = new SequenceMessageComposer(Result<NextMessage>.Failure("fallback boom"));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, failingFallback);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<ComposedMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

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

    [Fact]
    public async Task ComposeAsync_FirstAttemptBad_LogsWarningBeforeRetrying()
    {
        var capturingLogger = new CapturingLogger<ValidatingMessageComposer>();
        var innerComposer = new SequenceMessageComposer(
            Result<NextMessage>.Success(BadMessage()),
            Result<NextMessage>.Success(CleanMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer, capturingLogger);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Contains(capturingLogger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    // The error text can carry raw model response content on the OpenAI path, so the log
    // records the failure category and the text itself never reaches a log sink. The
    // correction still carries it to the next attempt, which the test above proves.
    [Fact]
    public async Task ComposeAsync_FirstAttemptResultFailure_LogsTheCategoryAndNotTheErrorText()
    {
        var capturingLogger = new CapturingLogger<ValidatingMessageComposer>();
        var innerComposer = new SequenceMessageComposer(
            Result<NextMessage>.Failure("Model returned cta_type 'call_now' but 'schedule_tour' was required."),
            Result<NextMessage>.Success(CleanMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer, capturingLogger);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Contains(capturingLogger.Entries, entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("returned a failure result", StringComparison.Ordinal));
        Assert.DoesNotContain(capturingLogger.Entries, entry => entry.Message.Contains("call_now", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ComposeAsync_BothAttemptsBad_LogsWarningBeforeFallingBack()
    {
        var capturingLogger = new CapturingLogger<ValidatingMessageComposer>();
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer, capturingLogger);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Contains(capturingLogger.Entries, entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("falling back", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ComposeAsync_FallbackAlsoUnsafe_LogsError()
    {
        var capturingLogger = new CapturingLogger<ValidatingMessageComposer>();
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()));
        var unsafeFallback = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, unsafeFallback, capturingLogger);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Contains(capturingLogger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // D24: the notes name the composer whose text was returned and the number of calls this
    // loop made to get it, so the fallback after two rejected attempts is visible as the
    // template composer on the third call rather than as a clean first attempt.
    [Fact]
    public async Task ComposeAsync_BothAttemptsBad_ReportsTheFallbackComposerAndEveryAttempt()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<ComposedMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Equal(new CompositionNotes(ComposerNames.Template, Attempts: 3, LocaleApplied: true), result.Value!.Notes);
    }

    [Fact]
    public async Task ComposeAsync_SecondAttemptClean_ReportsTheInnerComposerAndBothAttempts()
    {
        var innerComposer = new SequenceMessageComposer(
            Result<NextMessage>.Success(BadMessage()),
            Result<NextMessage>.Success(CleanMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<ComposedMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Equal(new CompositionNotes(SequenceMessageComposer.Name, Attempts: 2, LocaleApplied: true), result.Value!.Notes);
    }

    // D28 addendum: a retry the first (rejected) attempt spent is still a retry this record
    // spent, so the winning second attempt's own count is not the whole story on its own.
    [Fact]
    public async Task ComposeAsync_FirstAttemptHasRetriesThenFailsValidation_SecondAttemptSucceeds_SumsNetworkRetries()
    {
        var innerComposer = new SequenceMessageComposer(
            Result<NextMessage>.Success(BadMessage()),
            Result<NextMessage>.Success(CleanMessage()))
        {
            NetworkRetries = [2, 0],
        };
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<ComposedMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Notes.NetworkRetries);
    }

    // The fallback composer makes no network call of its own (NetworkRetries stays null),
    // but retries spent on the two rejected attempts before it are still real: they carry
    // through onto the fallback's notes rather than disappearing because the composer that
    // finally answered has nothing of its own to add.
    [Fact]
    public async Task ComposeAsync_BothAttemptsHadRetriesThenFail_FallsBackAndReportsTheAccumulatedRetries()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()))
        {
            NetworkRetries = [1],
        };
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        Result<ComposedMessage> result = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Notes.NetworkRetries);
    }
}
