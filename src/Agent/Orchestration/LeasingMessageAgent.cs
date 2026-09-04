using Agent.Common;
using Agent.Composition;
using Agent.Decisions;
using Agent.Domain;
using Agent.Safety;

namespace Agent.Orchestration;

// Holds no business rules of its own (DESIGN.md section 5): every decision is
// delegated to the component that owns it. `.Value` on Option/Result is used
// deliberately, not defensively re-checked - both already throw a clear
// InvalidOperationException on the "should never happen" case, so duplicating
// that guard here would just be dead code no test could reach honestly.
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
            var suppressedDiagnostics = new AgentDiagnostics(
                consentDecision.ConsentVerified,
                FairHousingCheckPassed: true,
                BrandStyleApplied: false,
                SafetyViolationCount: 0);

            return new AgentRunResult(new AgentOutput(NextMessage: null, nextAction), suppressedDiagnostics);
        }

        CommunicationChannel channel = channelSelector.Select(prospectCase.ChannelPreferences, prospectCase.Consent).Value;

        Result<NextMessage> composeResult = await composer.ComposeAsync(prospectCase, channel, cancellationToken: cancellationToken);
        NextMessage composedMessage = composeResult.Value;

        DateTimeOffset sendAt = scheduler.Resolve(prospectCase.Input.LastInteraction, prospectCase.Input.TimeZoneId, channel);
        NextMessage finalMessage = composedMessage with { SendAt = sendAt };

        SafetyValidationResult validation = validator.Validate(finalMessage, prospectCase.Assertions.Constraints);
        var diagnostics = new AgentDiagnostics(
            consentDecision.ConsentVerified,
            validation.FairHousingCheckPassed,
            BrandStyleApplied: true,
            validation.Violations.Count);

        return new AgentRunResult(new AgentOutput(finalMessage, nextAction), diagnostics);
    }
}
