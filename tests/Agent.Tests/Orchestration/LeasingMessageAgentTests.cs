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

    // SampleProspectCases.Minimal asserts no state, so a test that wants a verdict in the map
    // has to say which state the record asserts. The constraints come along unchanged: the
    // opt-out gate of D40 reads them.
    private static ProspectCase Asserting(params string[] requiredStates)
    {
        ProspectCase minimal = SampleProspectCases.Minimal();

        return minimal with { Assertions = new CaseAssertions(requiredStates, minimal.ConstraintsOrEmpty) };
    }

    [Fact]
    public async Task RunAsync_Sample1_ProducesSmsAndStartCadence()
    {
        LeasingMessageAgent agent = RealAgentFactory.BuildRealAgent();
        ProspectCase sample1 = RealAgentFactory.ReadSampleCases()[0];

        AgentRunResult result = await agent.RunAsync(sample1, ReferenceTime);

        Assert.NotNull(result.Output.NextMessage);
        Assert.Equal(CommunicationChannel.Sms, result.Output.NextMessage!.Channel);
        Assert.Equal(DateTimeOffset.Parse("2025-12-09T09:00:00-06:00"), result.Output.NextMessage.SendAt);
        Assert.Equal("start_cadence", result.Output.NextAction.Type);

        // D42: sample 1 asserts all three states this program has a check for, and each is
        // answered by its own source rather than claimed. brand_style_applied is the check of
        // D42's second part, not the literal true it replaced.
        Assert.Equal(
            new Dictionary<string, RequiredStateVerdict>(StringComparer.Ordinal)
            {
                ["consent_verified"] = RequiredStateVerdict.Earned,
                ["fair_housing_check_passed"] = RequiredStateVerdict.Earned,
                ["brand_style_applied"] = RequiredStateVerdict.Earned,
            },
            result.Diagnostics.RequiredStates);
        Assert.Empty(result.Diagnostics.BrandStyleFailures!);
        Assert.Equal(0, result.Diagnostics.SafetyViolationCount);
        Assert.Equal(SuppressionReason.None, result.Diagnostics.SuppressionReason);
        Assert.Equal(new ActionPlanNotes(HorizonBranch.Short, 32, ActionSource.CatalogRow), result.Diagnostics.ActionPlan);

        // D22: the send is explained by the same run. Sample 1's last interaction is before
        // the reference time, so the reference time is the floor (A4), and its zone reaches
        // 09:00 once on that day, so the slot is exact (A20). No slot row answers a prospect at
        // new on sms, so the channel's hour set the time.
        Assert.Equal(
            new ScheduleNotes(ScheduleFloor.ReferenceTime, TimeZoneInfo.FindSystemTimeZoneById("America/Chicago").Id, SlotResolution.Exact, SendSlotSource.ChannelDefault),
            result.Diagnostics.Schedule);
    }

    [Fact]
    public async Task RunAsync_Sample2_ProducesEmailAndFollowUpInDays()
    {
        LeasingMessageAgent agent = RealAgentFactory.BuildRealAgent();
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
        LeasingMessageAgent agent = RealAgentFactory.BuildRealAgent();
        ProspectCase suppressedCase = Asserting("consent_verified", "fair_housing_check_passed", "brand_style_applied") with
        {
            Consent = new ConsentPreferences(EmailOptIn: false, SmsOptIn: false, VoiceOptIn: false),
        };

        AgentRunResult result = await agent.RunAsync(suppressedCase, ReferenceTime);

        Assert.Equal(CommunicationChannel.None, result.Output.NextMessage!.Channel);
        Assert.Null(result.Output.NextMessage.Body);
        Assert.Null(result.Output.NextMessage.SendAt);
        Assert.Equal("no_op", result.Output.NextAction.Type);
        Assert.Equal("no_contact_consent", result.Output.NextAction.Reason);

        // consent_verified is earned by the record reaching step 1, the consent-driven channel
        // selection that owns the state, whichever way that step answered (D57).
        // Neither the safety validator nor the brand-style validator ran, because there is
        // no message: not evaluated is the honest answer and it is not a pass (A15).
        Assert.Equal(RequiredStateVerdict.Earned, result.Diagnostics.RequiredStates["consent_verified"]);
        Assert.Equal(RequiredStateVerdict.NotEvaluated, result.Diagnostics.RequiredStates["fair_housing_check_passed"]);
        Assert.Equal(RequiredStateVerdict.NotEvaluated, result.Diagnostics.RequiredStates["brand_style_applied"]);
        Assert.Null(result.Diagnostics.BrandStyleFailures);
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
        LeasingMessageAgent agent = RealAgentFactory.BuildRealAgent();
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
            new ChannelSelector(),
            new TemplateMessageComposer(),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner(),
            capturingLogger);
        const string PersonaMarker = "persona-marker-7f3a";
        const string StageMarker = "stage-marker-7f3a";
        ProspectCase unmatched = SampleProspectCases.Minimal() with { Persona = PersonaMarker, LifecycleStage = StageMarker };

        await agent.RunAsync(unmatched, ReferenceTime);

        // The enum renders as its C# name, the way channel=Sms already does on the composed
        // line; the diagnostics file is where the snake_case spelling lives (D3).
        // Warning, because the action is one no catalog row states and a person has to review it;
        // exactly one line, naming the source and the branch. The persona and stage are the
        // record's own free text, so they stay out of the log and go to the review queue instead.
        CapturingLogger<LeasingMessageAgent>.LogEntry fallback = Assert.Single(
            capturingLogger.Entries,
            entry => entry.Message.Contains($"generic row ({ActionSource.GenericRowNoMatch})", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, fallback.Level);
        Assert.Contains($"branch={HorizonBranch.Short}", fallback.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(PersonaMarker, fallback.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(StageMarker, fallback.Message, StringComparison.Ordinal);
    }

    // The other side of the same branch: a record whose row states the action it needs is not
    // a fallback, and nothing says it was.
    [Fact]
    public async Task RunAsync_CatalogRowAnsweredThePlan_LogsNoFallback()
    {
        var capturingLogger = new CapturingLogger<LeasingMessageAgent>();
        var agent = new LeasingMessageAgent(
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
        LeasingMessageAgent agent = new LeasingMessageAgent(
            new ChannelSelector(),
            new SequenceMessageComposer(Agent.Common.Result<NextMessage>.Failure("nothing composable")),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner());
        ProspectCase impossibleCase = Asserting("consent_verified", "brand_style_applied");

        AgentRunResult result = await agent.RunAsync(impossibleCase, ReferenceTime);

        Assert.Equal(CommunicationChannel.None, result.Output.NextMessage!.Channel);
        Assert.Equal("start_cadence", result.Output.NextAction.Type);
        Assert.Equal(RequiredStateVerdict.Earned, result.Diagnostics.RequiredStates["consent_verified"]);
        Assert.Equal(RequiredStateVerdict.NotEvaluated, result.Diagnostics.RequiredStates["brand_style_applied"]);
        Assert.Null(result.Diagnostics.BrandStyleFailures);
        Assert.Equal(0, result.Diagnostics.SafetyViolationCount);
        Assert.Equal(SuppressionReason.CompositionFailed, result.Diagnostics.SuppressionReason);
    }

    // D1 end to end: a record carrying only the three required members runs to a message
    // (A12 greeting without a name, A6 UTC, A7 long horizon), never to an exception.
    [Fact]
    public async Task RunAsync_OnlyRequiredMembers_ComposesInUtcWithTheLongHorizonAction()
    {
        LeasingMessageAgent agent = RealAgentFactory.BuildRealAgent();
        var bareCase = new ProspectCase(
            "bare",
            new ConsentPreferences(SmsOptIn: true),
            [CommunicationChannel.Sms]);

        AgentRunResult result = await agent.RunAsync(bareCase, ReferenceTime);

        Assert.NotNull(result.Output.NextMessage);
        Assert.Equal(CommunicationChannel.Sms, result.Output.NextMessage!.Channel);
        Assert.Equal(TimeSpan.Zero, result.Output.NextMessage.SendAt!.Value.Offset);
        Assert.Equal("follow_up_in_days", result.Output.NextAction.Type);

        // D1: this record carries no assertions at all, so it asserts no state and the map is
        // the complete answer to a list with no items.
        Assert.Empty(result.Diagnostics.RequiredStates);
    }

    [Fact]
    public async Task RunAsync_FinalSafetyValidationFindsViolations_SuppressesMessage()
    {
        SafetyValidationResult violatingResult = FairHousingFailure();
        LeasingMessageAgent agent = RealAgentFactory.BuildRealAgent(new FixedSafetyValidator(violatingResult));
        ProspectCase prospectCase = Asserting("fair_housing_check_passed", "brand_style_applied");

        AgentRunResult result = await agent.RunAsync(prospectCase, ReferenceTime);

        Assert.Equal(CommunicationChannel.None, result.Output.NextMessage!.Channel);
        Assert.Null(result.Output.NextMessage.Body);
        Assert.Equal("start_cadence", result.Output.NextAction.Type);
        Assert.Equal(RequiredStateVerdict.NotEarned, result.Diagnostics.RequiredStates["fair_housing_check_passed"]);

        // The draft existed and was checked before the safety gate discarded it, so its brand
        // verdict is a real answer, not "not evaluated". D43's review queue is where the draft
        // itself goes.
        Assert.Equal(RequiredStateVerdict.Earned, result.Diagnostics.RequiredStates["brand_style_applied"]);
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
        LeasingMessageAgent agent = RealAgentFactory.BuildRealAgent(new FixedSafetyValidator(OptOutFailureOnly()));
        ProspectCase prospectCase = Asserting("fair_housing_check_passed");

        AgentRunResult result = await agent.RunAsync(prospectCase, ReferenceTime);

        Assert.Equal(RequiredStateVerdict.Earned, result.Diagnostics.RequiredStates["fair_housing_check_passed"]);
        Assert.Equal(1, result.Diagnostics.SafetyViolationCount);
        Assert.Equal(SuppressionReason.SafetyViolation, result.Diagnostics.SuppressionReason);
        Assert.Equal(CommunicationChannel.None, result.Output.NextMessage!.Channel);
    }

    // D16: the CLI's batch loop is the one owner of the TaskId scope. The agent opening a
    // second one rendered every line as "TaskId=x TaskId=x" (the retrospective's logging
    // defect 1); a library caller that wants correlation opens its own scope, as the CLI does.
    [Fact]
    public async Task RunAsync_AnyOutcome_OpensNoLogScopeOfItsOwn()
    {
        var capturingLogger = new CapturingLogger<LeasingMessageAgent>();
        var agent = new LeasingMessageAgent(
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

        Assert.Contains(capturingLogger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("not contactable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_FinalSafetyValidationFindsViolations_LogsWarning()
    {
        var capturingLogger = new CapturingLogger<LeasingMessageAgent>();
        SafetyValidationResult violatingResult = FairHousingFailure();
        var agent = new LeasingMessageAgent(
            new ChannelSelector(),
            new TemplateMessageComposer(),
            new FixedSafetyValidator(violatingResult),
            new SendScheduler(),
            new NextActionPlanner(),
            capturingLogger);
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        await agent.RunAsync(prospectCase, ReferenceTime);

        Assert.Contains(capturingLogger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Suppressing message: final safety validation found", StringComparison.Ordinal));
    }

    // D24 and the Phase 4 check: the record says which implementation wrote its message.
    // The real agent is wired with the template composer, so an offline run names it on
    // every record that has a message.
    [Fact]
    public async Task RunAsync_TemplateComposer_RecordsWhichImplementationWroteTheMessage()
    {
        LeasingMessageAgent agent = RealAgentFactory.BuildRealAgent();
        ProspectCase sample1 = RealAgentFactory.ReadSampleCases()[0];

        AgentRunResult result = await agent.RunAsync(sample1, ReferenceTime);

        Assert.Equal(new CompositionNotes(ComposerNames.Template, Attempts: 1, LocaleApplied: true), result.Diagnostics.Composition);
    }

    // D1 on the suppression path, the other side of RunAsync_OnlyRequiredMembers_...: a record
    // carrying only the three required members states no assertions object at all, and one
    // whose consent object opts in to nothing is not contactable. It asserts no state, so the
    // map is empty rather than absent, and reading the states of a record with no assertions
    // does not throw.
    [Fact]
    public async Task RunAsync_OnlyRequiredMembersAndNoConsent_SuppressesAndAssertsNoState()
    {
        LeasingMessageAgent agent = RealAgentFactory.BuildRealAgent();
        var bareCase = new ProspectCase("bare", new ConsentPreferences(), []);

        AgentRunResult result = await agent.RunAsync(bareCase, ReferenceTime);

        Assert.Equal(SuppressionReason.NoContactConsent, result.Diagnostics.SuppressionReason);
        Assert.Empty(result.Diagnostics.RequiredStates);
    }

    // D42 and docs/CODE_REVIEW.md, against the record that actually asserts it: hold-out 6
    // names renewal_offer_loaded and carries a renewal_offer_id a rule could obviously be
    // fitted to. No rule is written for it, because the hold-out is an evaluation set and
    // nothing is fitted to it (D9, A19). The name is recorded, by name, as one this program
    // has no check for, and the two states that do have checks are answered beside it.
    [Fact]
    public async Task RunAsync_RecordAssertingAStateWithNoCheck_RecordsItByNameAsNotEarned()
    {
        LeasingMessageAgent agent = RealAgentFactory.BuildRealAgent();
        ProspectCase renewalCase = RealAgentFactory.ReadCases("holdout_12.jsonl")
            .Single(prospectCase => prospectCase.TaskId == "resident_renewal_undecided_followup");

        AgentRunResult result = await agent.RunAsync(renewalCase, ReferenceTime);

        Assert.Equal(
            new Dictionary<string, RequiredStateVerdict>(StringComparer.Ordinal)
            {
                ["consent_verified"] = RequiredStateVerdict.Earned,
                ["fair_housing_check_passed"] = RequiredStateVerdict.Earned,
                ["renewal_offer_loaded"] = RequiredStateVerdict.NoCheckDefined,
            },
            result.Diagnostics.RequiredStates);
    }

    // D39 and D42: brand style is a diagnostic and never suppresses. The composed message
    // breaks the exclamation rule and nothing else, so it is still sent, the state is recorded
    // not earned, and the row names the rule that failed rather than saying only "false".
    [Fact]
    public async Task RunAsync_MessageBreaksABrandRule_StillSendsAndRecordsWhichRuleFailed()
    {
        LeasingMessageAgent agent = new LeasingMessageAgent(
            new ChannelSelector(),
            new SequenceMessageComposer(Agent.Common.Result<NextMessage>.Success(
                new NextMessage(CommunicationChannel.Sms, Body: "Hi Taylor! Tours are open! Reply STOP to opt out."))),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner());

        AgentRunResult result = await agent.RunAsync(Asserting("brand_style_applied"), ReferenceTime);

        Assert.Equal(CommunicationChannel.Sms, result.Output.NextMessage!.Channel);
        Assert.Equal(SuppressionReason.None, result.Diagnostics.SuppressionReason);
        Assert.Equal(RequiredStateVerdict.NotEarned, result.Diagnostics.RequiredStates["brand_style_applied"]);
        Assert.Equal([BrandStyleRule.ExclamationLimit], result.Diagnostics.BrandStyleFailures);
    }

    // The same fact in the log, for the same reason the generic-row fallback is logged: the
    // diagnostics file only exists when the run was given --diagnostics, and the log is
    // always on.
    [Fact]
    public async Task RunAsync_MessageBreaksABrandRule_LogsWhichRuleFailed()
    {
        var capturingLogger = new CapturingLogger<LeasingMessageAgent>();
        var agent = new LeasingMessageAgent(
            new ChannelSelector(),
            new SequenceMessageComposer(Agent.Common.Result<NextMessage>.Success(
                new NextMessage(CommunicationChannel.Sms, Subject: "Tour Oak Ridge", Body: "Reply STOP to opt out."))),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner(),
            capturingLogger);

        await agent.RunAsync(SampleProspectCases.Minimal(), ReferenceTime);

        Assert.Contains(
            capturingLogger.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains(nameof(BrandStyleRule.SubjectMatchesChannel), StringComparison.Ordinal));
    }

    // The other side of that branch: a message that breaks no rule says nothing about brand
    // style in the log.
    [Fact]
    public async Task RunAsync_MessageAppliesEveryBrandRule_LogsNoBrandStyleWarning()
    {
        var capturingLogger = new CapturingLogger<LeasingMessageAgent>();
        var agent = new LeasingMessageAgent(
            new ChannelSelector(),
            new TemplateMessageComposer(),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner(),
            capturingLogger);

        await agent.RunAsync(SampleProspectCases.Minimal(), ReferenceTime);

        Assert.DoesNotContain(capturingLogger.Entries, entry => entry.Message.Contains("Brand style", StringComparison.Ordinal));
    }

    // A record with no message has no composer to name.
    [Fact]
    public async Task RunAsync_NoConsentedChannel_RecordsNoComposition()
    {
        LeasingMessageAgent agent = RealAgentFactory.BuildRealAgent();
        ProspectCase prospectCase = SampleProspectCases.Minimal() with
        {
            Consent = new ConsentPreferences(EmailOptIn: false, SmsOptIn: false, VoiceOptIn: false),
        };

        AgentRunResult result = await agent.RunAsync(prospectCase, ReferenceTime);

        Assert.Null(result.Diagnostics.Composition);
    }

    // D48 through the same wiring the CLI builds: the record's own city_interest is written
    // into the body by the template composer, so every attempt and the fallback are refused.
    // The refusal carries the draft out instead of destroying it, and step 5's own validation
    // is what names the violations, so this record reports a safety violation rather than the
    // composition failure it used to report.
    [Fact]
    public async Task RunAsync_ComposeLoopRefusesEveryDraft_SuppressesAsASafetyViolationAndKeepsTheDraft()
    {
        LeasingMessageAgent agent = RealAgentFactory.BuildRealAgent();
        ProspectCase steeringCase = SampleProspectCases.Minimal(cityInterest: "families only") with
        {
            Assertions = new CaseAssertions(
                ["fair_housing_check_passed"],
                SampleProspectCases.Minimal().ConstraintsOrEmpty),
        };

        AgentRunResult result = await agent.RunAsync(steeringCase, ReferenceTime);

        Assert.Equal(SuppressionReason.SafetyViolation, result.Diagnostics.SuppressionReason);
        Assert.Equal(RequiredStateVerdict.NotEarned, result.Diagnostics.RequiredStates["fair_housing_check_passed"]);
        Assert.Equal(CommunicationChannel.None, result.Output.NextMessage!.Channel);
        Assert.NotNull(result.RejectedDraft);
        Assert.Contains("families only", result.RejectedDraft!.Message.Body!, StringComparison.Ordinal);
        Assert.Equal(SafetyCheck.FairHousing, Assert.Single(result.RejectedDraft.Violations).Check);
    }

    // The regression guard for the behavior that must not change: a composer that produced no
    // draft at all is still a composition failure, still queues nothing, and still records the
    // fair-housing state as not evaluated, because nothing was ever checked.
    [Fact]
    public async Task RunAsync_ComposerProducedNoDraft_StaysACompositionFailureWithNothingToQueue()
    {
        LeasingMessageAgent agent = new LeasingMessageAgent(
            new ChannelSelector(),
            new SequenceMessageComposer(Agent.Common.Result<NextMessage>.Failure("nothing composable")),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner());

        AgentRunResult result = await agent.RunAsync(Asserting("fair_housing_check_passed"), ReferenceTime);

        Assert.Equal(SuppressionReason.CompositionFailed, result.Diagnostics.SuppressionReason);
        Assert.Equal(RequiredStateVerdict.NotEvaluated, result.Diagnostics.RequiredStates["fair_housing_check_passed"]);
        Assert.Null(result.RejectedDraft);
    }

    // The other producer of a rejected draft: a message that reached step 5 as a composed
    // message and failed there. Every failed check contributes its own row, so the queue entry
    // names both checks rather than one line the reviewer has to re-derive.
    [Fact]
    public async Task RunAsync_ComposedMessageFailsFinalValidation_KeepsTheDraftWithEveryFailedCheck()
    {
        var twoFailures = new SafetyValidationResult(
            SafetyCheckResult.Failed(SafetyCheck.OptOutInstructions, ["Missing required opt-out instructions."]),
            SafetyCheckResult.Passed(SafetyCheck.SocialSecurityNumber),
            SafetyCheckResult.NotApplicable(SafetyCheck.LongDigitRun),
            SafetyCheckResult.Failed(SafetyCheck.FairHousing, ["Body contains protected-class or steering language: 'disability'."]));
        LeasingMessageAgent agent = RealAgentFactory.BuildRealAgent(new FixedSafetyValidator(twoFailures));

        AgentRunResult result = await agent.RunAsync(SampleProspectCases.Minimal(), ReferenceTime);

        Assert.Equal(SuppressionReason.SafetyViolation, result.Diagnostics.SuppressionReason);
        Assert.NotNull(result.RejectedDraft);
        Assert.Equal(
            [SafetyCheck.OptOutInstructions, SafetyCheck.FairHousing],
            result.RejectedDraft!.Violations.Select(violation => violation.Check));
    }

    // The refusal's own reason reaches the log: the compose-validate loop and step 5 are two
    // gates on one record, and a reader of the log should see the handoff rather than only the
    // second gate's verdict.
    [Fact]
    public async Task RunAsync_ComposeLoopRefusesEveryDraft_LogsTheRefusalReason()
    {
        var capturingLogger = new CapturingLogger<LeasingMessageAgent>();
        var templateComposer = new TemplateMessageComposer();
        var agent = new LeasingMessageAgent(
            new ChannelSelector(),
            new ValidatingMessageComposer(templateComposer, new SafetyValidator(), templateComposer),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner(),
            capturingLogger);

        await agent.RunAsync(SampleProspectCases.Minimal(cityInterest: "families only"), ReferenceTime);

        Assert.Contains(
            capturingLogger.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("refused", StringComparison.OrdinalIgnoreCase));
    }

    // D66, on the record it is about: D48's steering record, whose own city_interest is
    // written into the body by the template fallback. The model attempt was abandoned at its
    // timeout and D34 sent it straight to the fallback, which reproduced the steering language,
    // and the loop refused its draft, so the record ships nothing and has no composition notes.
    // The call the vendor billed for is still a call this record made, and it now leaves the
    // agent on the row itself instead of vanishing with the notes.
    [Fact]
    public async Task RunAsync_ComposeLoopRefusesEveryDraft_StillReportsWhatTheRunSpent()
    {
        var abandoningComposer = new SequenceMessageComposer(Result<NextMessage>.Failure("OpenAI call exceeded its budget."))
        {
            ModelCosts = [new ModelCostNotes(Calls: 1, CompletedCalls: 0, InputTokens: 0, OutputTokens: 0)],
        };
        var templateComposer = new TemplateMessageComposer();
        var agent = new LeasingMessageAgent(
            new ChannelSelector(),
            new ValidatingMessageComposer(abandoningComposer, new SafetyValidator(), templateComposer),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner());

        AgentRunResult result = await agent.RunAsync(SampleProspectCases.Minimal(cityInterest: "families only"), ReferenceTime);

        Assert.Equal(SuppressionReason.SafetyViolation, result.Diagnostics.SuppressionReason);
        Assert.Null(result.Diagnostics.Composition);
        Assert.Equal(new ModelCostNotes(Calls: 1, CompletedCalls: 0, InputTokens: 0, OutputTokens: 0), result.Diagnostics.ModelCost);
    }

    // D66's other suppression with a cost: no draft anywhere, so the record is a composition
    // failure and not a refusal. The suppressed row is built by a different code path from the
    // one above and has to answer for the same spend.
    [Fact]
    public async Task RunAsync_CompositionFailedAfterAbandonedCalls_StillReportsWhatTheRunSpent()
    {
        var abandoningComposer = new SequenceMessageComposer(Result<NextMessage>.Failure("OpenAI call exceeded its budget."))
        {
            ModelCosts = [new ModelCostNotes(Calls: 1, CompletedCalls: 0, InputTokens: 0, OutputTokens: 0)],
        };
        var failingFallback = new SequenceMessageComposer(Result<NextMessage>.Failure("nothing composable"));
        var agent = new LeasingMessageAgent(
            new ChannelSelector(),
            new ValidatingMessageComposer(abandoningComposer, new SafetyValidator(), failingFallback),
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner());

        AgentRunResult result = await agent.RunAsync(SampleProspectCases.Minimal(), ReferenceTime);

        Assert.Equal(SuppressionReason.CompositionFailed, result.Diagnostics.SuppressionReason);
        Assert.Null(result.Diagnostics.Composition);
        Assert.Equal(new ModelCostNotes(Calls: 1, CompletedCalls: 0, InputTokens: 0, OutputTokens: 0), result.Diagnostics.ModelCost);
    }

    // The record that never reached a composer at all: no consented channel, so no call was
    // made and there is nothing to report. Null is the absence of a measurement, and this is
    // the case that keeps the two moved members from reading as a zero on every suppression.
    [Fact]
    public async Task RunAsync_NoConsentedChannel_ReportsNoSpendAtAll()
    {
        LeasingMessageAgent agent = RealAgentFactory.BuildRealAgent();
        ProspectCase prospectCase = SampleProspectCases.Minimal() with
        {
            Consent = new ConsentPreferences(EmailOptIn: false, SmsOptIn: false, VoiceOptIn: false),
        };

        AgentRunResult result = await agent.RunAsync(prospectCase, ReferenceTime);

        Assert.Null(result.Diagnostics.ModelCost);
        Assert.Null(result.Diagnostics.NetworkRetries);
    }
}
