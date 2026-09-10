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

    // The message this loop returned, when it returned one. A test that expects a message
    // says so once here rather than restating the outcome type at every call site.
    private static ComposedMessage ComposedOf(ComposeOutcome outcome) =>
        Assert.IsType<ComposeOutcome.Composed>(outcome).Message;

    [Fact]
    public async Task ComposeAsync_FirstAttemptClean_ReturnsFirstAttemptWithoutRetry()
    {
        NextMessage cleanMessage = CleanMessage();
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(cleanMessage));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Same(cleanMessage, ComposedOf(outcome).Message);
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

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Same(cleanMessage, ComposedOf(outcome).Message);
        Assert.Equal(2, innerComposer.CallCount);
    }

    [Fact]
    public async Task ComposeAsync_BothAttemptsBad_FallsBackToSafeComposer()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        SafetyValidationResult finalValidation = Validator.Validate(ComposedOf(outcome).Message, prospectCase.ConstraintsOrEmpty);
        Assert.Empty(finalValidation.Violations);
    }

    [Fact]
    public async Task ComposeAsync_ComposerKeepsFailing_FallsBackToSafeComposer()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Failure("boom"));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.IsType<ComposeOutcome.Composed>(outcome);
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

    // D48: nothing unsafe ships, and the draft is no longer destroyed on the way out. The
    // refusal carries the fallback draft, which is the one the orchestrator validates and
    // the review queue holds.
    [Fact]
    public async Task ComposeAsync_FallbackAlsoUnsafe_RefusesAndCarriesTheFallbackDraftOut()
    {
        NextMessage fallbackDraft = BadMessage();
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()));
        var unsafeFallback = new SequenceMessageComposer(Result<NextMessage>.Success(fallbackDraft));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, unsafeFallback);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        ComposeOutcome.Refused refused = Assert.IsType<ComposeOutcome.Refused>(outcome);
        Assert.Same(fallbackDraft, refused.Draft);
        Assert.NotEmpty(refused.Error);
    }

    // The other exit: the fallback built no message at all, so there is no draft to carry and
    // no queue row to write. Failed and Refused are different facts and stay different here.
    [Fact]
    public async Task ComposeAsync_FallbackComposerFailsToCompose_FailsWithNoDraft()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()));
        var failingFallback = new SequenceMessageComposer(Result<NextMessage>.Failure("fallback boom"));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, failingFallback);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        ComposeOutcome.Failed failed = Assert.IsType<ComposeOutcome.Failed>(outcome);
        Assert.NotEmpty(failed.Error);
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

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Equal(new CompositionNotes(ComposerNames.Template, Attempts: 3, LocaleApplied: true), ComposedOf(outcome).Notes);
    }

    [Fact]
    public async Task ComposeAsync_SecondAttemptClean_ReportsTheInnerComposerAndBothAttempts()
    {
        var innerComposer = new SequenceMessageComposer(
            Result<NextMessage>.Success(BadMessage()),
            Result<NextMessage>.Success(CleanMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Equal(new CompositionNotes(SequenceMessageComposer.Name, Attempts: 2, LocaleApplied: true), ComposedOf(outcome).Notes);
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

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Equal(2, outcome.NetworkRetries);
    }

    // The Composed-then-rejected branch above already sums a discarded attempt's retries onto
    // the winner; this is the other branch, an attempt that made no message at all (a
    // Result.Failure, the same shape a wrong-cta_type or malformed-JSON response takes on the
    // OpenAI path). A retry that attempt spent is still a retry this record spent, whether or
    // not the attempt produced a message (Claude Code review, PR #26).
    [Fact]
    public async Task ComposeAsync_FirstAttemptFailsWithRetriesThenSecondSucceeds_SumsNetworkRetriesFromTheFailedAttempt()
    {
        var innerComposer = new SequenceMessageComposer(
            Result<NextMessage>.Failure("boom"),
            Result<NextMessage>.Success(CleanMessage()))
        {
            NetworkRetries = [2, 0],
        };
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Equal(2, outcome.NetworkRetries);
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

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Equal(2, outcome.NetworkRetries);
    }

    // D62: this loop owns the per-record sum, for the reason it owns Attempts (D24 addendum).
    // A composer knows what its own call cost and nothing about the calls the attempts beside
    // it made, so a record's cost is a fact only the code that ran every attempt has.
    [Fact]
    public async Task ComposeAsync_FirstAttemptCostsTokensThenFailsValidation_SumsTheModelCostOntoTheWinner()
    {
        var innerComposer = new SequenceMessageComposer(
            Result<NextMessage>.Success(BadMessage()),
            Result<NextMessage>.Success(CleanMessage()))
        {
            ModelCosts =
            [
                new ModelCostNotes(Calls: 1, CompletedCalls: 1, InputTokens: 11, OutputTokens: 7),
                new ModelCostNotes(Calls: 1, CompletedCalls: 1, InputTokens: 13, OutputTokens: 5),
            ],
        };
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Equal(
            new ModelCostNotes(Calls: 2, CompletedCalls: 2, InputTokens: 24, OutputTokens: 12),
            outcome.ModelCost);
    }

    // The 2026-09-08 live run, in the shape this loop sees it: both attempts abandoned at the
    // timeout, the template fallback answers, and the record still states that two calls were
    // made and no tokens came back. The fallback has no cost of its own, so without the
    // accumulation the bill would vanish behind a clean-looking template row.
    [Fact]
    public async Task ComposeAsync_BothAttemptsAbandonedAtTheirTimeout_FallsBackAndKeepsTheCountedCalls()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Failure("OpenAI call exceeded its budget."))
        {
            ModelCosts = [new ModelCostNotes(Calls: 1, CompletedCalls: 0, InputTokens: 0, OutputTokens: 0)],
        };
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Equal(ComposerNames.Template, ComposedOf(outcome).Notes.Composer);
        Assert.Equal(new ModelCostNotes(Calls: 2, CompletedCalls: 0, InputTokens: 0, OutputTokens: 0), outcome.ModelCost);
    }

    // Nothing on the record's path made a model call, so there is no measurement to report and
    // the sum stays null rather than becoming a zero nobody measured.
    [Fact]
    public async Task ComposeAsync_NoAttemptMadeAModelCall_LeavesTheModelCostUnmeasured()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(CleanMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Null(outcome.ModelCost);
    }

    // D66: the exit this loop had no way to report through. Both attempts were rejected on
    // safety and the fallback's own draft was too, so nothing ships and the record carries no
    // composition notes at all - and the two calls it made, and the retries under them, are
    // still what this record spent. Reported on the outcome itself, which every case answers
    // for, rather than on notes only a returned message has.
    [Fact]
    public async Task ComposeAsync_FallbackAlsoUnsafe_TheRefusalCarriesWhatTheAttemptsSpent()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()))
        {
            NetworkRetries = [1],
            ModelCosts = [new ModelCostNotes(Calls: 1, CompletedCalls: 1, InputTokens: 11, OutputTokens: 7)],
        };
        var unsafeFallback = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, unsafeFallback);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        var refused = Assert.IsType<ComposeOutcome.Refused>(outcome);
        Assert.Equal(new ModelCostNotes(Calls: 2, CompletedCalls: 2, InputTokens: 22, OutputTokens: 14), refused.ModelCost);
        Assert.Equal(2, refused.NetworkRetries);
    }

    // The other exit with no message: the fallback built none either, so the record is a
    // composition failure rather than a refusal. Both attempts were abandoned at their
    // timeout, and an abandoned call returns no completion to read a retry count off (D28's
    // addendum), so the retries stay null - no measurement, not a measured zero - while the
    // calls themselves are still counted.
    [Fact]
    public async Task ComposeAsync_FallbackProducesNoMessage_TheFailureCarriesWhatTheAttemptsSpent()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Failure("OpenAI call exceeded its budget."))
        {
            ModelCosts = [new ModelCostNotes(Calls: 1, CompletedCalls: 0, InputTokens: 0, OutputTokens: 0)],
        };
        var failingFallback = new SequenceMessageComposer(Result<NextMessage>.Failure("nothing composable"));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, failingFallback);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        var failed = Assert.IsType<ComposeOutcome.Failed>(outcome);
        Assert.Equal(new ModelCostNotes(Calls: 2, CompletedCalls: 0, InputTokens: 0, OutputTokens: 0), failed.ModelCost);
        Assert.Null(failed.NetworkRetries);
    }
}
