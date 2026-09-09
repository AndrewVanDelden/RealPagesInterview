using Agent.Common;
using Agent.Composition;
using Agent.Decisions;
using Agent.Domain;
using Agent.Ingest;
using Agent.Orchestration;
using Agent.Safety;
using Agent.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Agent.Tests.Orchestration;

public class LeasingMessageAgentTests
{
    // D10: the reference time is a value the caller passes, never a clock the agent reads.
    private static readonly DateTimeOffset ReferenceTime = DateTimeOffset.Parse("2025-12-09T00:00:00-06:00");

    // D38: the validator answers per check, so a fake stands one up the same way the real
    // one does. Only the fair-housing check failed here.
    private static SafetyValidationResult FairHousingFailure() =>
        new(SafetyCheckResult.NotApplicable(SafetyCheck.OptOutInstructions),
            SafetyCheckResult.Passed(SafetyCheck.SocialSecurityNumber),
            SafetyCheckResult.NotApplicable(SafetyCheck.LongDigitRun),
            SafetyCheckResult.Failed(SafetyCheck.FairHousing, ["Body contains protected-class or steering language: 'disability'."]));

    // The defect D38 names: fair_housing_check_passed was violations.Count == 0, so this
    // record recorded a fair-housing failure that never happened.
    private static SafetyValidationResult OptOutFailureOnly() =>
        new(SafetyCheckResult.Failed(SafetyCheck.OptOutInstructions, ["Missing required opt-out instructions."]),
            SafetyCheckResult.Passed(SafetyCheck.SocialSecurityNumber),
            SafetyCheckResult.NotApplicable(SafetyCheck.LongDigitRun),
            SafetyCheckResult.Passed(SafetyCheck.FairHousing));

    [Fact]
    public async Task RunAsync_Sample1_ProducesSmsAndStartCadence()
    {
        IMessageAgent agent = RealAgentFactory.BuildRealAgent();
        ProspectCase sample1 = RealAgentFactory.ReadSampleCases()[0];

        AgentRunResult result = await agent.RunAsync(sample1, ReferenceTime);

        Assert.NotNull(result.Output.NextMessage);
        Assert.Equal(CommunicationChannel.Sms, result.Output.NextMessage!.Channel);
        Assert.Equal(DateTimeOffset.Parse("2025-12-09T09:00:00-06:00"), result.Output.NextMessage.SendAt);
        Assert.Equal("start_cadence", result.Output.NextAction.Type);
        Assert.True(result.Diagnostics.ConsentVerified);
        Assert.True(result.Diagnostics.FairHousingCheckPassed);
        Assert.True(result.Diagnostics.BrandStyleApplied);
        Assert.Equal(0, result.Diagnostics.SafetyViolationCount);
        Assert.Equal(SuppressionReason.None, result.Diagnostics.SuppressionReason);
        Assert.Equal(new ActionPlanNotes(HorizonBranch.Short, 32, ActionSource.CatalogRow), result.Diagnostics.ActionPlan);

        // D22: the send is explained by the same run. Sample 1's last interaction is before
        // the reference time, so the reference time is the floor (A4), and its zone reaches
        // 09:00 once on that day, so the slot is exact (A20).
        Assert.Equal(
            new ScheduleNotes(ScheduleFloor.ReferenceTime, TimeZoneInfo.FindSystemTimeZoneById("America/Chicago").Id, SlotResolution.Exact),
            result.Diagnostics.Schedule);
    }

    [Fact]
    public async Task RunAsync_Sample2_ProducesEmailAndFollowUpInDays()
    {
        IMessageAgent agent = RealAgentFactory.BuildRealAgent();
        ProspectCase sample2 = RealAgentFactory.ReadSampleCases()[1];

        AgentRunResult result = await agent.RunAsync(sample2, ReferenceTime);

        Assert.NotNull(result.Output.NextMessage);
        Assert.Equal(CommunicationChannel.Email, result.Output.NextMessage!.Channel);
        Assert.Equal("follow_up_in_days", result.Output.NextAction.Type);
        Assert.Equal(3, result.Output.NextAction.Value);
    }

    // A2 and D2: consent first. Not contactable is a no_op with its reason and a
    // next_message object with channel none; nothing else runs.
    [Fact]
    public async Task RunAsync_NoConsentedChannel_EmitsNoneMessageAndNoOpWithReason()
    {
        IMessageAgent agent = RealAgentFactory.BuildRealAgent();
        ProspectCase suppressedCase = SampleProspectCases.Minimal() with
        {
            Consent = new ConsentPreferences(EmailOptIn: false, SmsOptIn: false, VoiceOptIn: false),
        };

        AgentRunResult result = await agent.RunAsync(suppressedCase, ReferenceTime);

        Assert.Equal(CommunicationChannel.None, result.Output.NextMessage!.Channel);
        Assert.Null(result.Output.NextMessage.Body);
        Assert.Null(result.Output.NextMessage.SendAt);
        Assert.Equal("no_op", result.Output.NextAction.Type);
        Assert.Equal("no_contact_consent", result.Output.NextAction.Reason);
        Assert.True(result.Diagnostics.ConsentVerified);
        Assert.Null(result.Diagnostics.FairHousingCheckPassed);
        Assert.False(result.Diagnostics.BrandStyleApplied);
        Assert.Equal(0, result.Diagnostics.SafetyViolationCount);
        Assert.Equal(SuppressionReason.NoContactConsent, result.Diagnostics.SuppressionReason);
        Assert.Null(result.Diagnostics.ActionPlan);
        Assert.Null(result.Diagnostics.Schedule);
    }

    // A8 through the whole agent: a record that states no persona and no lifecycle stage has
    // no catalog row, so the generic row answers and the fallback is recorded rather than
    // passed off as a row's decision (playbook step 43). With no move date the horizon is
    // long and unstated (A7), so the notes carry a null day count, not a zero.
    [Fact]
    public async Task RunAsync_RecordWithNoPersonaStageOrMoveDate_RecordsTheGenericRowFallback()
    {
        IMessageAgent agent = RealAgentFactory.BuildRealAgent();
        ProspectCase unclassifiable = SampleProspectCases.Minimal() with
        {
            Persona = null,
            LifecycleStage = null,
            Input = SampleProspectCases.Minimal().ContextOrEmpty with { MoveDateTarget = null },
        };

        AgentRunResult result = await agent.RunAsync(unclassifiable, ReferenceTime);

        Assert.Equal(new ActionPlanNotes(HorizonBranch.Long, null, ActionSource.GenericRowNoMatch), result.Diagnostics.ActionPlan);
        Assert.Equal(ActionTypes.FollowUpInDays, result.Output.NextAction.Type);
    }

    // Playbook step 43: the fallback is logged when it fires. The diagnostics file records it
    // too, but that file only exists when the run was given --diagnostics, and the log is
    // always on.
    [Fact]
    public async Task RunAsync_GenericRowAnsweredThePlan_LogsThatTheFallbackFired()
    {
        var capturingLogger = new CapturingLogger<LeasingMessageAgent>();
        var agent = new LeasingMessageAgent(
            new ConsentGate(),
            new ChannelSelector(),
            new TemplateMessageComposer(),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner(),
            capturingLogger);
        ProspectCase unmatched = SampleProspectCases.Minimal() with { Persona = "resident", LifecycleStage = "renewal" };

        await agent.RunAsync(unmatched, ReferenceTime);

        // The enum renders as its C# name, the way channel=Sms already does on the composed
        // line; the diagnostics file is where the snake_case spelling lives (D3).
        Assert.Contains(capturingLogger.Entries, entry => entry.Message.Contains($"generic row ({ActionSource.GenericRowNoMatch})", StringComparison.Ordinal));
    }

    // The other side of the same branch: a record whose row states the action it needs is not
    // a fallback, and nothing says it was.
    [Fact]
    public async Task RunAsync_CatalogRowAnsweredThePlan_LogsNoFallback()
    {
        var capturingLogger = new CapturingLogger<LeasingMessageAgent>();
        var agent = new LeasingMessageAgent(
            new ConsentGate(),
            new ChannelSelector(),
            new TemplateMessageComposer(),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner(),
            capturingLogger);

        await agent.RunAsync(SampleProspectCases.Minimal(), ReferenceTime);

        Assert.DoesNotContain(capturingLogger.Entries, entry => entry.Message.Contains("generic row", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_ComposerCannotProduceAnyValidMessage_SuppressesMessageInsteadOfThrowing()
    {
        IMessageAgent agent = new LeasingMessageAgent(
            new ConsentGate(),
            new ChannelSelector(),
            new SequenceMessageComposer(Agent.Common.Result<NextMessage>.Failure("nothing composable")),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner());
        ProspectCase impossibleCase = SampleProspectCases.Minimal();

        AgentRunResult result = await agent.RunAsync(impossibleCase, ReferenceTime);

        Assert.Equal(CommunicationChannel.None, result.Output.NextMessage!.Channel);
        Assert.Equal("start_cadence", result.Output.NextAction.Type);
        Assert.True(result.Diagnostics.ConsentVerified);
        Assert.Null(result.Diagnostics.FairHousingCheckPassed);
        Assert.False(result.Diagnostics.BrandStyleApplied);
        Assert.Equal(0, result.Diagnostics.SafetyViolationCount);
        Assert.Equal(SuppressionReason.CompositionFailed, result.Diagnostics.SuppressionReason);
    }

    // D1 end to end: a record carrying only the three required members runs to a message
    // (A12 greeting without a name, A6 UTC, A7 long horizon), never to an exception.
    [Fact]
    public async Task RunAsync_OnlyRequiredMembers_ComposesInUtcWithTheLongHorizonAction()
    {
        IMessageAgent agent = RealAgentFactory.BuildRealAgent();
        var bareCase = new ProspectCase(
            "bare",
            new ConsentPreferences(SmsOptIn: true),
            [CommunicationChannel.Sms]);

        AgentRunResult result = await agent.RunAsync(bareCase, ReferenceTime);

        Assert.NotNull(result.Output.NextMessage);
        Assert.Equal(CommunicationChannel.Sms, result.Output.NextMessage!.Channel);
        Assert.Equal(TimeSpan.Zero, result.Output.NextMessage.SendAt!.Value.Offset);
        Assert.Equal("follow_up_in_days", result.Output.NextAction.Type);
        Assert.True(result.Diagnostics.ConsentVerified);
    }

    [Fact]
    public async Task RunAsync_FinalSafetyValidationFindsViolations_SuppressesMessage()
    {
        SafetyValidationResult violatingResult = FairHousingFailure();
        IMessageAgent agent = RealAgentFactory.BuildRealAgent(new FixedSafetyValidator(violatingResult));
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        AgentRunResult result = await agent.RunAsync(prospectCase, ReferenceTime);

        Assert.Equal(CommunicationChannel.None, result.Output.NextMessage!.Channel);
        Assert.Null(result.Output.NextMessage.Body);
        Assert.Equal("start_cadence", result.Output.NextAction.Type);
        Assert.False(result.Diagnostics.FairHousingCheckPassed);
        Assert.True(result.Diagnostics.BrandStyleApplied);
        Assert.Equal(1, result.Diagnostics.SafetyViolationCount);
        Assert.Equal(SuppressionReason.SafetyViolation, result.Diagnostics.SuppressionReason);

        // AgentDiagnostics.cs: Composition is null on a record that has no message. A
        // record the final safety check suppresses has no message either, the same as the
        // other two suppression cases below (RunAsync_ComposerCannotProduceAnyValidMessage_...
        // and RunAsync_NoConsentedChannel_RecordsNoComposition).
        Assert.Null(result.Diagnostics.Composition);
    }

    // D38: fair_housing_check_passed now comes from the fair-housing check's own verdict,
    // never from "no violations at all". A record that only omitted its opt-out line is
    // still suppressed, and no longer records a fair-housing failure that never happened.
    [Fact]
    public async Task RunAsync_OnlyTheOptOutCheckFails_RecordsFairHousingAsPassedAndStillSuppresses()
    {
        IMessageAgent agent = RealAgentFactory.BuildRealAgent(new FixedSafetyValidator(OptOutFailureOnly()));
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        AgentRunResult result = await agent.RunAsync(prospectCase, ReferenceTime);

        Assert.True(result.Diagnostics.FairHousingCheckPassed);
        Assert.Equal(1, result.Diagnostics.SafetyViolationCount);
        Assert.Equal(SuppressionReason.SafetyViolation, result.Diagnostics.SuppressionReason);
        Assert.Equal(CommunicationChannel.None, result.Output.NextMessage!.Channel);
    }

    [Fact]
    public async Task RunAsync_CancellationRequested_PropagatesCancellationFromComposer()
    {
        IMessageAgent agent = new LeasingMessageAgent(
            new ConsentGate(),
            new ChannelSelector(),
            new ThrowsOnCancellationComposer(),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner());
        ProspectCase prospectCase = SampleProspectCases.Minimal();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => agent.RunAsync(prospectCase, ReferenceTime, cts.Token));
    }

    // D16: the CLI's batch loop is the one owner of the TaskId scope. The agent opening a
    // second one rendered every line as "TaskId=x TaskId=x" (the retrospective's logging
    // defect 1); a library caller that wants correlation opens its own scope, as the CLI does.
    [Fact]
    public async Task RunAsync_AnyOutcome_OpensNoLogScopeOfItsOwn()
    {
        var capturingLogger = new CapturingLogger<LeasingMessageAgent>();
        var agent = new LeasingMessageAgent(
            new ConsentGate(),
            new ChannelSelector(),
            new TemplateMessageComposer(),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner(),
            capturingLogger);
        ProspectCase prospectCase = SampleProspectCases.Minimal() with { TaskId = "correlation-check" };

        await agent.RunAsync(prospectCase, ReferenceTime);

        Assert.Empty(capturingLogger.Scopes);
    }

    // Sprint 8's audit named this exact gap: neither LeasingMessageAgent nor
    // ValidatingMessageComposer had a catch of their own, so a caller other than CliRunner
    // (which only sees the exception after it has already propagated all the way up) would
    // get zero log visibility into an unhandled exception. Logs it here, at the source,
    // then rethrows unchanged - callers still see the same exception, they just aren't the
    // only place it's ever recorded.
    [Fact]
    public async Task RunAsync_ComposerThrowsUnhandledException_LogsErrorAndRethrows()
    {
        var capturingLogger = new CapturingLogger<LeasingMessageAgent>();
        var agent = new LeasingMessageAgent(
            new ConsentGate(),
            new ChannelSelector(),
            new ThrowsComposer(),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner(),
            capturingLogger);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.RunAsync(prospectCase, ReferenceTime));

        Assert.Contains(capturingLogger.Entries, entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException);
    }

    // Cancellation is not a bug: it must still propagate (unchanged from before this catch
    // existed), but it must not be recorded as an Error-level "unhandled exception" - that
    // would make a clean shutdown indistinguishable from a real crash in the log.
    [Fact]
    public async Task RunAsync_CancellationRequested_DoesNotLogAsError()
    {
        var capturingLogger = new CapturingLogger<LeasingMessageAgent>();
        var agent = new LeasingMessageAgent(
            new ConsentGate(),
            new ChannelSelector(),
            new ThrowsOnCancellationComposer(),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner(),
            capturingLogger);
        ProspectCase prospectCase = SampleProspectCases.Minimal();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => agent.RunAsync(prospectCase, ReferenceTime, cts.Token));

        Assert.DoesNotContain(capturingLogger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task RunAsync_NoConsentedChannel_LogsInformationForSuppression()
    {
        var capturingLogger = new CapturingLogger<LeasingMessageAgent>();
        var agent = new LeasingMessageAgent(
            new ConsentGate(),
            new ChannelSelector(),
            new TemplateMessageComposer(),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner(),
            capturingLogger);
        ProspectCase suppressedCase = SampleProspectCases.Minimal() with
        {
            Consent = new ConsentPreferences(EmailOptIn: false, SmsOptIn: false, VoiceOptIn: false),
        };

        await agent.RunAsync(suppressedCase, ReferenceTime);

        Assert.Contains(capturingLogger.Entries, entry => entry.Level == LogLevel.Information);
    }

    [Fact]
    public async Task RunAsync_FinalSafetyValidationFindsViolations_LogsWarning()
    {
        var capturingLogger = new CapturingLogger<LeasingMessageAgent>();
        SafetyValidationResult violatingResult = FairHousingFailure();
        var agent = new LeasingMessageAgent(
            new ConsentGate(),
            new ChannelSelector(),
            new TemplateMessageComposer(),
            new FixedSafetyValidator(violatingResult),
            new SendScheduler(),
            new NextActionPlanner(),
            capturingLogger);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        await agent.RunAsync(prospectCase, ReferenceTime);

        Assert.Contains(capturingLogger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    // D24 and the Phase 4 check: the record says which implementation wrote its message.
    // The real agent is wired with the template composer, so an offline run names it on
    // every record that has a message.
    [Fact]
    public async Task RunAsync_TemplateComposer_RecordsWhichImplementationWroteTheMessage()
    {
        IMessageAgent agent = RealAgentFactory.BuildRealAgent();
        ProspectCase sample1 = RealAgentFactory.ReadSampleCases()[0];

        AgentRunResult result = await agent.RunAsync(sample1, ReferenceTime);

        Assert.Equal(new CompositionNotes(ComposerNames.Template, Attempts: 1, LocaleApplied: true), result.Diagnostics.Composition);
    }

    // A record with no message has no composer to name.
    [Fact]
    public async Task RunAsync_NoConsentedChannel_RecordsNoComposition()
    {
        IMessageAgent agent = RealAgentFactory.BuildRealAgent();
        ProspectCase prospectCase = SampleProspectCases.Minimal() with
        {
            Consent = new ConsentPreferences(EmailOptIn: false, SmsOptIn: false, VoiceOptIn: false),
        };

        AgentRunResult result = await agent.RunAsync(prospectCase, ReferenceTime);

        Assert.Null(result.Diagnostics.Composition);
    }
}
