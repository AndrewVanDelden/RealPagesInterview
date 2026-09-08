using Agent.Common;
using Agent.Composition;
using Agent.Decisions;
using Agent.Domain;
using Agent.Safety;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<LeasingMessageAgent> log = logger.OrNullLogger();

    // D16: no log scope is opened here. The caller's batch loop (CliRunner) is the one
    // owner of the TaskId scope; a second one here rendered every line as
    // "TaskId=x TaskId=x". A library caller that wants correlation opens its own scope.
    public async Task<AgentRunResult> RunAsync(ProspectCase prospectCase, DateTimeOffset referenceTime, CancellationToken cancellationToken = default)
    {
        // Sprint 8's audit named this gap by name: without a catch here, only CliRunner
        // (which happens to wrap agent.RunAsync in its own try/catch) ever sees an
        // unhandled exception. A future caller (a web API, a queue worker) integrating
        // this class directly, without its own try/catch, would get zero log signal that
        // anything went wrong. Logged here, at the source, then rethrown unchanged -
        // callers still see the exact same exception; they're no longer the only place
        // it's ever recorded. Cancellation is excluded: it isn't a bug, and logging it as
        // Error would make a clean shutdown indistinguishable from a real crash.
        try
        {
            return await RunUnguardedAsync(prospectCase, referenceTime, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Unhandled exception during record processing.");
            throw;
        }
    }

    private async Task<AgentRunResult> RunUnguardedAsync(ProspectCase prospectCase, DateTimeOffset referenceTime, CancellationToken cancellationToken)
    {
        ProspectContext context = prospectCase.ContextOrEmpty;

        // Step 1: consent first (D2). Not contactable is a no_op with its reason and
        // nothing else runs.
        ConsentDecision consentDecision = consentGate.Evaluate(prospectCase.Consent, prospectCase.ChannelPreferences);

        if (!consentDecision.IsContactable)
        {
            log.LogInformation("Suppressing message: prospect is not contactable.");
            return Suppressed(
                consentDecision,
                SuppressionReason.NoContactConsent,
                new NextAction(ActionTypes.NoOp, Reason: SuppressionReason.NoContactConsent.ToWireName()),
                actionPlan: null);
        }

        CommunicationChannel channel = channelSelector.Select(prospectCase.ChannelPreferences, prospectCase.Consent).Value;

        // Step 2: plan from the horizon (A7), counted in the record's local date (D10).
        DateOnly referenceDate = TimeZones.ToLocalDate(referenceTime, context.TimeZoneId);
        PlannedAction planned = planner.Plan(prospectCase.Persona, prospectCase.LifecycleStage, context.MoveDateTarget, referenceDate);
        NextAction nextAction = planned.Action;
        var actionPlan = new ActionPlanNotes(planned.Branch, planned.HorizonDays, planned.Source);

        // Playbook step 43: the catalog's fallback is defined, and firing it is recorded
        // where a reader will see it. The diagnostics file carries the same fact, but only
        // when the run was given --diagnostics.
        if (planned.Source != ActionSource.CatalogRow)
        {
            log.LogInformation(
                "Next action came from the generic row ({Source}): persona={Persona}, stage={LifecycleStage}, branch={Branch}.",
                planned.Source,
                prospectCase.Persona,
                prospectCase.LifecycleStage,
                planned.Branch);
        }

        // Step 3: compose.
        Result<NextMessage> composeResult = await composer.ComposeAsync(prospectCase, channel, cancellationToken: cancellationToken);

        if (!composeResult.IsSuccess)
        {
            log.LogWarning("Suppressing message: composition failed ({Error}).", composeResult.Error);
            return Suppressed(consentDecision, SuppressionReason.CompositionFailed, nextAction, actionPlan);
        }

        // Step 4: schedule (A4, A5).
        DateTimeOffset sendAt = scheduler.Resolve(referenceTime, context.LastInteraction, context.TimeZoneId, channel);
        NextMessage finalMessage = composeResult.Value with { SendAt = sendAt };

        // Step 5: validate. An unsafe or off-brand draft never leaves the agent (DESIGN.md
        // section 5): ValidatingMessageComposer already guarantees a clean message under
        // normal wiring, but this is the orchestrator's own gate, not borrowed trust in the
        // composer's cooperation.
        SafetyValidationResult validation = validator.Validate(finalMessage, prospectCase.ConstraintsOrEmpty);

        bool hasViolations = validation.Violations.Count > 0;
        var diagnostics = new AgentDiagnostics(
            consentDecision.ConsentVerified,
            validation.FairHousingCheckPassed,
            BrandStyleApplied: true,
            validation.Violations.Count,
            hasViolations ? SuppressionReason.SafetyViolation : SuppressionReason.None,
            actionPlan);

        if (hasViolations)
        {
            log.LogWarning("Suppressing message: final safety validation found {ViolationCount} violation(s).", validation.Violations.Count);
            return new AgentRunResult(new AgentOutput(SuppressedMessage(), nextAction), diagnostics);
        }

        // Step 6: emit.
        log.LogInformation("Message composed: channel={Channel}, nextAction={NextAction}.", channel, nextAction.Type);
        return new AgentRunResult(new AgentOutput(finalMessage, nextAction), diagnostics);
    }

    // D3: suppression on the wire is a next_message object with channel none and every
    // other member null, the oracle's own spelling, never a null object.
    private static NextMessage SuppressedMessage() => new(CommunicationChannel.None);

    private static AgentRunResult Suppressed(ConsentDecision consentDecision, SuppressionReason reason, NextAction nextAction, ActionPlanNotes? actionPlan)
    {
        var diagnostics = new AgentDiagnostics(
            consentDecision.ConsentVerified,
            FairHousingCheckPassed: null,
            BrandStyleApplied: false,
            SafetyViolationCount: 0,
            reason,
            actionPlan);

        return new AgentRunResult(new AgentOutput(SuppressedMessage(), nextAction), diagnostics);
    }
}
