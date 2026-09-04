using Agent.Composition;
using Agent.Decisions;
using Agent.Domain;
using Agent.Ingest;
using Agent.Orchestration;
using Agent.Safety;

namespace Agent.Tests.TestSupport;

internal static class RealAgentFactory
{
    private static readonly string SampleFilePath = Path.Combine(AppContext.BaseDirectory, "TestData", "sample.jsonl");

    public static IMessageAgent BuildRealAgent(ISafetyValidator? finalValidator = null)
    {
        var templateComposer = new TemplateMessageComposer();
        IMessageComposer validatingComposer = new ValidatingMessageComposer(templateComposer, new SafetyValidator(), templateComposer);

        return new LeasingMessageAgent(
            new ConsentGate(),
            new ChannelSelector(),
            validatingComposer,
            finalValidator ?? new SafetyValidator(),
            new SendScheduler(),
            new NextActionPlanner());
    }

    public static IReadOnlyList<ProspectCase> ReadSampleCases()
    {
        using var reader = new StreamReader(SampleFilePath);
        return new JsonlRecordReader().ReadAll(reader);
    }
}
