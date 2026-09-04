using Agent.Common;
using Agent.Composition;
using Agent.Decisions;
using Agent.Domain;
using Agent.Safety;

namespace Agent.Orchestration;

// Holds no business rules of its own (DESIGN.md section 5): every decision is
// delegated to the component that owns it. `.Value` on Option<CommunicationChannel>
// at the channel-selection step is used deliberately, not defensively re-checked:
// IsContactable already guarantees a consented channel exists, so re-checking here
// would be dead code no test could reach honestly. composeResult and the final
// safety validation are different: both are real, reachable failure modes (an
// unsalvageable compose-validate loop, or a violation slipping past composition),
// so both are handled explicitly below rather than trusted with .Value.
public sealed class LeasingMessageAgent(
    IConsentGate consentGate,
    IChannelSelector channelSelector,
    IMessageComposer composer,
    ISafetyValidator validator,
    ISendScheduler scheduler,
    INextActionPlanner planner) : IMessageAgent
{
    public async Task<AgentRunResult> RunAsync(ProspectCase prospectCase, CancellationToken cancellationToken = default)
    {
        ConsentDecision consentDecision = consentGate.Evaluate(prospectCase.Consent, prospectCase.ChannelPreferences);
        NextAction nextAction = planner.Plan(prospectCase.Input.MoveDateTarget, prospectCase.Input.LastInteraction, prospectCase.Input.TimeZoneId);

        if (!consentDecision.IsContactable)
        {
            return Suppressed(consentDecision, nextAction);
        }

        CommunicationChannel channel = channelSelector.Select(prospectCase.ChannelPreferences, prospectCase.Consent).Value;

        Result<NextMessage> composeResult = await composer.ComposeAsync(prospectCase, channel, cancellationToken: cancellationToken);

        if (!composeResult.IsSuccess)
        {
            return Suppressed(consentDecision, nextAction);
        }

        DateTimeOffset sendAt = scheduler.Resolve(prospectCase.Input.LastInteraction, prospectCase.Input.TimeZoneId, channel);
        NextMessage finalMessage = composeResult.Value with { SendAt = sendAt };

        SafetyValidationResult validation = validator.Validate(finalMessage, prospectCase.Assertions.Constraints);
        var diagnostics = new AgentDiagnostics(
            consentDecision.ConsentVerified,
            validation.FairHousingCheckPassed,
            BrandStyleApplied: true,
            validation.Violations.Count);

        // An unsafe or off-brand draft never leaves the agent (DESIGN.md section 4):
        // ValidatingMessageComposer already guarantees a clean message under normal
        // wiring, but this is the orchestrator's own gate, not borrowed trust in the
        // composer's cooperation.
        NextMessage? outputMessage = validation.Violations.Count == 0 ? finalMessage : null;

        return new AgentRunResult(new AgentOutput(outputMessage, nextAction), diagnostics);
    }

    private static AgentRunResult Suppressed(ConsentDecision consentDecision, NextAction nextAction)
    {
        var diagnostics = new AgentDiagnostics(
            consentDecision.ConsentVerified,
            FairHousingCheckPassed: null,
            BrandStyleApplied: false,
            SafetyViolationCount: 0);

        return new AgentRunResult(new AgentOutput(NextMessage: null, nextAction), diagnostics);
    }
}
