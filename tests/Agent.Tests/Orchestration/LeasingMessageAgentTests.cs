using Agent.Composition;
using Agent.Decisions;
using Agent.Domain;
using Agent.Ingest;
using Agent.Orchestration;
using Agent.Safety;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Orchestration;

public class LeasingMessageAgentTests
{
    private static readonly string SampleFilePath = Path.Combine(AppContext.BaseDirectory, "TestData", "sample.jsonl");

    private static IMessageAgent BuildRealAgent()
    {
        var templateComposer = new TemplateMessageComposer();
        IMessageComposer validatingComposer = new ValidatingMessageComposer(templateComposer, new SafetyValidator(), templateComposer);

        return new LeasingMessageAgent(
            new ConsentGate(),
            new ChannelSelector(),
            validatingComposer,
            new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner());
    }

    private static IReadOnlyList<ProspectCase> ReadSampleCases()
    {
        using var reader = new StreamReader(SampleFilePath);
        return new JsonlRecordReader().ReadAll(reader);
    }

    [Fact]
    public async Task RunAsync_Sample1_ProducesSmsAndStartCadence()
    {
        IMessageAgent agent = BuildRealAgent();
        ProspectCase sample1 = ReadSampleCases()[0];

        AgentRunResult result = await agent.RunAsync(sample1);

        Assert.NotNull(result.Output.NextMessage);
        Assert.Equal(CommunicationChannel.Sms, result.Output.NextMessage!.Channel);
        Assert.NotNull(result.Output.NextMessage.SendAt);
        Assert.Equal("start_cadence", result.Output.NextAction.Type);
        Assert.True(result.Diagnostics.ConsentVerified);
        Assert.True(result.Diagnostics.FairHousingCheckPassed);
        Assert.True(result.Diagnostics.BrandStyleApplied);
        Assert.Equal(0, result.Diagnostics.SafetyViolationCount);
    }

    [Fact]
    public async Task RunAsync_Sample2_ProducesEmailAndFollowUpInDays()
    {
        IMessageAgent agent = BuildRealAgent();
        ProspectCase sample2 = ReadSampleCases()[1];

        AgentRunResult result = await agent.RunAsync(sample2);

        Assert.NotNull(result.Output.NextMessage);
        Assert.Equal(CommunicationChannel.Email, result.Output.NextMessage!.Channel);
        Assert.Equal("follow_up_in_days", result.Output.NextAction.Type);
        Assert.Equal(3, result.Output.NextAction.Value);
    }

    [Fact]
    public async Task RunAsync_NoConsentedChannel_SuppressesMessageButStillPlansNextAction()
    {
        IMessageAgent agent = BuildRealAgent();
        ProspectCase suppressedCase = SampleProspectCases.Minimal() with
        {
            Consent = new ConsentPreferences(EmailOptIn: false, SmsOptIn: false, VoiceOptIn: false),
        };

        AgentRunResult result = await agent.RunAsync(suppressedCase);

        Assert.Null(result.Output.NextMessage);
        Assert.NotNull(result.Output.NextAction);
        Assert.True(result.Diagnostics.ConsentVerified);
        Assert.False(result.Diagnostics.BrandStyleApplied);
        Assert.Equal(0, result.Diagnostics.SafetyViolationCount);
    }

    [Fact]
    public async Task RunAsync_ComposerCannotProduceAnyValidMessage_ThrowsInvalidOperationException()
    {
        IMessageAgent agent = BuildRealAgent();
        ProspectCase impossibleCase = SampleProspectCases.Minimal(firstName: "");

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.RunAsync(impossibleCase));
    }
}
