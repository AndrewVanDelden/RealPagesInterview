using Agent.Common;
using Agent.Composition;
using Agent.Decisions;
using Agent.Domain;
using Agent.Orchestration;
using Agent.Safety;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Orchestration;

// How many times the shared validator is asked about one record. The compose-validate loop
// and the agent's final gate answer the same question about the same draft, so a record whose
// draft the loop already passed is validated once, and every other draft is validated at the
// final gate.
public class FinalValidationCountTests
{
    private static readonly DateTimeOffset ReferenceTime = DateTimeOffset.Parse("2025-12-09T00:00:00-06:00");

    private static NextMessage SteeringMessage() =>
        new(CommunicationChannel.Sms, null, null, "This community is families only.", null);

    private static LeasingMessageAgent AgentWith(IMessageComposer composer, ISafetyValidator validator) =>
        new(new ChannelSelector(), composer, validator, new SendScheduler(), new NextActionPlanner());

    // Every labeled set, under the wiring CliRunner builds, with each set's documented
    // reference time. A record that ships a message is the case the loop already validated.
    [Theory]
    [InlineData("sample.jsonl", "2025-12-09T00:00:00-06:00")]
    [InlineData("holdout_12.jsonl", "2025-12-09T00:00:00-06:00")]
    [InlineData("synthetic_12.jsonl", "2026-03-07T12:00:00Z")]
    public async Task RunAsync_ProductionWiring_ValidatesEveryComposedRecordOnce(string fileName, string referenceTime)
    {
        DateTimeOffset now = DateTimeOffset.Parse(referenceTime);
        var callsByComposedRecord = new List<(string TaskId, int Calls)>();

        foreach (ProspectCase prospectCase in RealAgentFactory.ReadCases(fileName))
        {
            var countingValidator = new CountingSafetyValidator(new SafetyValidator());
            AgentRunResult result = await RealAgentFactory.BuildRealAgent(sharedValidator: countingValidator).RunAsync(prospectCase, now);

            if (result.Diagnostics.SuppressionReason == SuppressionReason.None)
            {
                callsByComposedRecord.Add((prospectCase.TaskId, countingValidator.ValidatedMessages.Count));
            }
        }

        Assert.NotEmpty(callsByComposedRecord);
        Assert.Equal(callsByComposedRecord.Select(row => (row.TaskId, 1)), callsByComposedRecord);
    }

    // A refused draft carries no verdict, so the final gate validates it: two model attempts
    // and the fallback inside the loop, then the scheduled draft once more at the final gate.
    [Fact]
    public async Task RunAsync_ComposeLoopRefusesTheDraft_ValidatesItAgainAtTheFinalGate()
    {
        var countingValidator = new CountingSafetyValidator(new SafetyValidator());
        LeasingMessageAgent agent = RealAgentFactory.BuildRealAgent(sharedValidator: countingValidator);

        AgentRunResult result = await agent.RunAsync(SampleProspectCases.Minimal(cityInterest: "families only"), ReferenceTime);

        Assert.Equal(SuppressionReason.SafetyViolation, result.Diagnostics.SuppressionReason);
        Assert.Equal(4, countingValidator.ValidatedMessages.Count);
        Assert.Same(result.RejectedDraft!.Message, countingValidator.ValidatedMessages[^1]);
        Assert.NotNull(countingValidator.ValidatedMessages[^1].SendAt);
    }

    // A composer that is not the loop hands over a message with no verdict, so the final gate
    // validates it and an unsafe one does not ship.
    [Fact]
    public async Task RunAsync_ComposerIsNotTheLoop_ValidatesItsMessageAtTheFinalGate()
    {
        var countingValidator = new CountingSafetyValidator(new SafetyValidator());
        LeasingMessageAgent agent = AgentWith(
            new SequenceMessageComposer(Result<NextMessage>.Success(SteeringMessage())),
            countingValidator);

        AgentRunResult result = await agent.RunAsync(SampleProspectCases.Minimal(), ReferenceTime);

        Assert.Equal(SuppressionReason.SafetyViolation, result.Diagnostics.SuppressionReason);
        Assert.Same(result.RejectedDraft!.Message, Assert.Single(countingValidator.ValidatedMessages));
    }

    // A wrapper that swaps the loop's clean draft for another keeps the loop's verdict on the
    // outcome, and that verdict answers for the draft the loop saw, not the one that arrived.
    [Fact]
    public async Task RunAsync_WrapperSwapsTheLoopsDraft_ValidatesTheSwappedDraftAtTheFinalGate()
    {
        var countingValidator = new CountingSafetyValidator(new SafetyValidator());
        var templateComposer = new TemplateMessageComposer();
        var swappingComposer = new RewritingComposer(new ValidatingMessageComposer(templateComposer, countingValidator, templateComposer))
        {
            RewriteOutcome = outcome =>
            {
                var composed = Assert.IsType<ComposeOutcome.Composed>(outcome);
                return composed with { Message = composed.Message with { Message = SteeringMessage() } };
            },
        };

        AgentRunResult result = await AgentWith(swappingComposer, countingValidator).RunAsync(SampleProspectCases.Minimal(), ReferenceTime);

        Assert.Equal(SuppressionReason.SafetyViolation, result.Diagnostics.SuppressionReason);
        Assert.Contains("families only", result.RejectedDraft!.Message.Body!, StringComparison.Ordinal);
        Assert.Equal(2, countingValidator.ValidatedMessages.Count);
        Assert.Same(result.RejectedDraft.Message, countingValidator.ValidatedMessages[1]);
    }

    // A wrapper that hands the loop looser constraints than the record states gets a verdict
    // for those constraints, and the final gate validates against the record's own.
    [Fact]
    public async Task RunAsync_WrapperRelaxesTheLoopsConstraints_ValidatesAgainstTheRecordsOwnConstraints()
    {
        var countingValidator = new CountingSafetyValidator(new SafetyValidator());
        var noOptOutLine = new NextMessage(CommunicationChannel.Sms, null, null, "Hi Taylor! Book a tour today.", null);
        var relaxingComposer = new RewritingComposer(new ValidatingMessageComposer(
            new SequenceMessageComposer(Result<NextMessage>.Success(noOptOutLine)),
            countingValidator,
            new TemplateMessageComposer()))
        {
            RewriteCase = prospectCase => prospectCase with
            {
                Assertions = new CaseAssertions([], prospectCase.ConstraintsOrEmpty with { IncludeOptOutInstructions = false }),
            },
        };

        AgentRunResult result = await AgentWith(relaxingComposer, countingValidator).RunAsync(SampleProspectCases.Minimal(), ReferenceTime);

        Assert.Equal(SuppressionReason.SafetyViolation, result.Diagnostics.SuppressionReason);
        Assert.Equal(SafetyCheck.OptOutInstructions, Assert.Single(result.RejectedDraft!.Violations).Check);
        Assert.Equal(2, countingValidator.ValidatedMessages.Count);
    }
}
