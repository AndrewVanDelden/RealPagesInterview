using Agent.Common;
using Agent.Composition;
using Agent.Decisions;
using Agent.Domain;
using Agent.Safety;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    INextActionPlanner planner,
    ILogger<LeasingMessageAgent>? logger = null) : IMessageAgent
{
    private readonly ILogger<LeasingMessageAgent> log = logger ?? NullLogger<LeasingMessageAgent>.Instance;

    public async Task<AgentRunResult> RunAsync(ProspectCase prospectCase, CancellationToken cancellationToken = default)
    {
        // Correlation ID for every log line emitted anywhere downstream of this call
        // (ValidatingMessageComposer, OpenAiMessageComposer) - opened here, not by the
        // caller, so any caller (the CLI today, a future API) gets it for free.
        using IDisposable? scope = log.BeginScope(new Dictionary<string, object> { ["TaskId"] = prospectCase.TaskId });

        ConsentDecision consentDecision = consentGate.Evaluate(prospectCase.Consent, prospectCase.ChannelPreferences);
        NextAction nextAction = planner.Plan(prospectCase.Input.MoveDateTarget, prospectCase.Input.LastInteraction, prospectCase.Input.TimeZoneId);

        if (!consentDecision.IsContactable)
        {
            log.LogInformation("Suppressing message: prospect is not contactable.");
            return Suppressed(consentDecision, nextAction);
        }

        CommunicationChannel channel = channelSelector.Select(prospectCase.ChannelPreferences, prospectCase.Consent).Value;

        Result<NextMessage> composeResult = await composer.ComposeAsync(prospectCase, channel, cancellationToken: cancellationToken);

        if (!composeResult.IsSuccess)
        {
            log.LogWarning("Suppressing message: composition failed ({Error}).", composeResult.Error);
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
        if (validation.Violations.Count > 0)
        {
            log.LogWarning("Suppressing message: final safety validation found {ViolationCount} violation(s).", validation.Violations.Count);
            return new AgentRunResult(new AgentOutput(NextMessage: null, nextAction), diagnostics);
        }

        log.LogInformation("Message composed: channel={Channel}, nextAction={NextAction}.", channel, nextAction.Type);
        return new AgentRunResult(new AgentOutput(finalMessage, nextAction), diagnostics);
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
