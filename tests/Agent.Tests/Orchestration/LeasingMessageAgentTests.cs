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

    [Fact]
    public async Task RunAsync_NoConsentedChannel_SuppressesMessageButStillPlansNextAction()
    {
        IMessageAgent agent = RealAgentFactory.BuildRealAgent();
        ProspectCase suppressedCase = SampleProspectCases.Minimal() with
        {
            Consent = new ConsentPreferences(EmailOptIn: false, SmsOptIn: false, VoiceOptIn: false),
        };

        AgentRunResult result = await agent.RunAsync(suppressedCase, ReferenceTime);

        Assert.Null(result.Output.NextMessage);
        Assert.NotNull(result.Output.NextAction);
        Assert.True(result.Diagnostics.ConsentVerified);
        Assert.Null(result.Diagnostics.FairHousingCheckPassed);
        Assert.False(result.Diagnostics.BrandStyleApplied);
        Assert.Equal(0, result.Diagnostics.SafetyViolationCount);
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

        Assert.Null(result.Output.NextMessage);
        Assert.NotNull(result.Output.NextAction);
        Assert.True(result.Diagnostics.ConsentVerified);
        Assert.Null(result.Diagnostics.FairHousingCheckPassed);
        Assert.False(result.Diagnostics.BrandStyleApplied);
        Assert.Equal(0, result.Diagnostics.SafetyViolationCount);
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
        var violatingResult = new SafetyValidationResult(["Body contains protected-class or steering language: 'disability'."], FairHousingCheckPassed: false);
        IMessageAgent agent = RealAgentFactory.BuildRealAgent(new FixedSafetyValidator(violatingResult));
        ProspectCase prospectCase = SampleProspectCases.Minimal();

        AgentRunResult result = await agent.RunAsync(prospectCase, ReferenceTime);

        Assert.Null(result.Output.NextMessage);
        Assert.NotNull(result.Output.NextAction);
        Assert.False(result.Diagnostics.FairHousingCheckPassed);
        Assert.True(result.Diagnostics.BrandStyleApplied);
        Assert.Equal(1, result.Diagnostics.SafetyViolationCount);
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

    // Correlation ID: any caller (the CLI today, a future API) gets the TaskId attached to
    // every log line emitted anywhere downstream of RunAsync for free, without needing to
    // pass it through composer/validator method signatures.
    [Fact]
    public async Task RunAsync_AnyOutcome_OpensLogScopeCarryingTaskId()
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

        Assert.Contains(capturingLogger.Scopes, scope =>
            scope is IReadOnlyDictionary<string, object> dict &&
            dict.TryGetValue("TaskId", out object? value) &&
            Equals(value, "correlation-check"));
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
        var violatingResult = new SafetyValidationResult(["Body contains protected-class or steering language: 'disability'."], FairHousingCheckPassed: false);
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
}
