using Agent.Common;
using Agent.Composition;
using Agent.Decisions;
using Agent.Domain;
using Agent.Ingest;
using Agent.Orchestration;
using Agent.Safety;
using Microsoft.Extensions.Logging.Abstractions;

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

    // Explicitly configured (even though this is just NullLoggerFactory) rather than left
    // unconfigured: LenientExpectedOutcomeConverter reaches a logger only through AgentLog's
    // ambient AsyncLocal (see its own remarks), and an unconfigured caller wouldn't fail
    // loudly if sample.jsonl ever gained a malformed `expected` field - it would just drop
    // the warning silently, the exact gap this project's Sprint 8 audit flagged. Making the
    // no-logger state explicit here means that gap can't reopen by accident.
    public static IReadOnlyList<ProspectCase> ReadSampleCases() => ReadCases("sample.jsonl");

    // Every line of the named fixture as a case; a fixture line that does not parse is a
    // test failure here, not a silently shorter set.
    public static IReadOnlyList<ProspectCase> ReadCases(string fileName)
    {
        using IDisposable scope = AgentLog.Configure(NullLoggerFactory.Instance);
        using var reader = new StreamReader(Path.Combine(AppContext.BaseDirectory, "TestData", fileName));
        return new JsonlRecordReader().ReadAll(reader).Select(result => result.Value).ToList();
    }
}
