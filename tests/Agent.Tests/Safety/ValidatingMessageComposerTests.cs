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

    // D34: an attempt that returned no message (a transport failure, a timeout, a malformed
    // completion) is not a content problem a second prompt could fix, so it goes straight to
    // the fallback and the inner composer is called once.
    [Fact]
    public async Task ComposeAsync_ComposerReturnsNoMessage_FallsBackWithoutASecondModelAttempt()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Failure("boom"));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.IsType<ComposeOutcome.Composed>(outcome);
        Assert.Equal(1, innerComposer.CallCount);
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
        Assert.Contains(innerComposer.LastPriorViolations, violation => violation.Contains("families only", StringComparison.Ordinal));
    }

    // D34: the fallback answers a no-message first attempt even where a second model attempt
    // would have come back clean, because the loop no longer makes one. The notes count the
    // one model attempt and the fallback, so Attempts reads 2 and not 3.
    [Fact]
    public async Task ComposeAsync_FirstAttemptReturnsNoMessage_TheFallbackAnswersOnTheSecondCall()
    {
        var innerComposer = new SequenceMessageComposer(
            Result<NextMessage>.Failure("Model returned cta_type 'call_now' but 'schedule_tour' was required."),
            Result<NextMessage>.Success(CleanMessage()));
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Equal(new CompositionNotes(ComposerNames.Template, Attempts: 2, LocaleApplied: true), ComposedOf(outcome).Notes);
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
    // records the failure category and the text itself never reaches a log sink. Since D34 it
    // reaches no later attempt either: a no-message attempt goes straight to the fallback.
    [Fact]
    public async Task ComposeAsync_FirstAttemptResultFailure_LogsTheCategoryAndNotTheErrorText()
    {
        var capturingLogger = new CapturingLogger<ValidatingMessageComposer>();
        var innerComposer = new SequenceMessageComposer(
            Result<NextMessage>.Failure("Model returned cta_type 'call_now' but 'schedule_tour' was required."));
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
    // not the attempt produced a message (Claude Code review, PR #26). D34 sends that attempt
    // straight to the fallback, which spends none, so its retries are the whole count.
    [Fact]
    public async Task ComposeAsync_FirstAttemptFailsWithRetries_TheFallbackCarriesTheFailedAttemptsRetries()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Failure("boom"))
        {
            NetworkRetries = [2],
        };
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Equal(ComposerNames.Template, ComposedOf(outcome).Notes.Composer);
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

    // The 2026-09-08 live run, in the shape this loop sees it since D34: the attempt is
    // abandoned at its timeout, the template fallback answers with no second model attempt,
    // and the record still states that one call was made and no tokens came back. The fallback
    // has no cost of its own, so without the accumulation the bill would vanish behind a
    // clean-looking template row.
    [Fact]
    public async Task ComposeAsync_AttemptAbandonedAtItsTimeout_FallsBackAndKeepsTheCountedCall()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Failure("OpenAI call exceeded its budget."))
        {
            ModelCosts = [new ModelCostNotes(Calls: 1, CompletedCalls: 0, InputTokens: 0, OutputTokens: 0)],
        };
        var composer = new ValidatingMessageComposer(innerComposer, Validator, FallbackComposer);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        Assert.Equal(ComposerNames.Template, ComposedOf(outcome).Notes.Composer);
        Assert.Equal(new ModelCostNotes(Calls: 1, CompletedCalls: 0, InputTokens: 0, OutputTokens: 0), outcome.ModelCost);
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
    // composition failure rather than a refusal. The attempt was abandoned at its timeout and
    // D34 sent it straight to the fallback, and an abandoned call returns no completion to read
    // a retry count off (D28's addendum), so the retries stay null - no measurement, not a
    // measured zero - while the call itself is still counted.
    [Fact]
    public async Task ComposeAsync_FallbackProducesNoMessage_TheFailureCarriesWhatTheAttemptSpent()
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
        Assert.Equal(new ModelCostNotes(Calls: 1, CompletedCalls: 0, InputTokens: 0, OutputTokens: 0), failed.ModelCost);
        Assert.Null(failed.NetworkRetries);
    }

    // D67 (a): a fallback composer that spends is billed like any attempt. The refusal adds the
    // fallback outcome's own counts to what the rejected attempts spent, the way WithAttempts
    // adds the winner's, so the three exits read alike. The template fallback this program
    // wires spends nothing, so this fake is the only fallback that can show the difference.
    [Fact]
    public async Task ComposeAsync_FallbackUnsafeAfterSpendingItsOwn_TheRefusalAddsTheFallbackSpend()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()))
        {
            NetworkRetries = [1],
            ModelCosts = [new ModelCostNotes(Calls: 1, CompletedCalls: 1, InputTokens: 11, OutputTokens: 7)],
        };
        var spendingUnsafeFallback = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()))
        {
            NetworkRetries = [2],
            ModelCosts = [new ModelCostNotes(Calls: 1, CompletedCalls: 1, InputTokens: 5, OutputTokens: 3)],
        };
        var composer = new ValidatingMessageComposer(innerComposer, Validator, spendingUnsafeFallback);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        var refused = Assert.IsType<ComposeOutcome.Refused>(outcome);
        Assert.Equal(new ModelCostNotes(Calls: 3, CompletedCalls: 3, InputTokens: 27, OutputTokens: 17), refused.ModelCost);
        Assert.Equal(4, refused.NetworkRetries);
    }

    // D67 (a), the other no-message exit: the fallback made a call that returned nothing
    // usable. That call is still one this record spent, so the failure carries it beside the
    // attempts' spend rather than reading the fallback's outcome for its error alone.
    [Fact]
    public async Task ComposeAsync_FallbackFailsAfterSpendingItsOwn_TheFailureAddsTheFallbackSpend()
    {
        var innerComposer = new SequenceMessageComposer(Result<NextMessage>.Success(BadMessage()))
        {
            NetworkRetries = [1],
            ModelCosts = [new ModelCostNotes(Calls: 1, CompletedCalls: 1, InputTokens: 11, OutputTokens: 7)],
        };
        var spendingFailingFallback = new SequenceMessageComposer(Result<NextMessage>.Failure("fallback call abandoned"))
        {
            NetworkRetries = [2],
            ModelCosts = [new ModelCostNotes(Calls: 1, CompletedCalls: 0, InputTokens: 0, OutputTokens: 0)],
        };
        var composer = new ValidatingMessageComposer(innerComposer, Validator, spendingFailingFallback);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        ComposeOutcome outcome = await composer.ComposeAsync(prospectCase, CommunicationChannel.Sms);

        var failed = Assert.IsType<ComposeOutcome.Failed>(outcome);
        Assert.Equal(new ModelCostNotes(Calls: 3, CompletedCalls: 2, InputTokens: 22, OutputTokens: 14), failed.ModelCost);
        Assert.Equal(4, failed.NetworkRetries);
    }
}
