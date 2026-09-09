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
// would be dead code no test could reach honestly. The compose outcome and the final
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
                prospectCase,
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

        // Step 3: compose. Three outcomes, and two of them carry a draft (D43): a composed
        // message, and one the compose-validate loop refused on safety. The refused draft is
        // scheduled and validated below exactly like a composed one, so step 5 is the one
        // place that names the violations; a composition that produced no draft at all is the
        // only one that short-circuits here, because there is nothing to validate.
        ComposeOutcome composeOutcome = await composer.ComposeAsync(prospectCase, channel, cancellationToken: cancellationToken);

        NextMessage draft;
        CompositionNotes? compositionNotes;

        switch (composeOutcome)
        {
            case ComposeOutcome.Composed composed:
                draft = composed.Message.Message;
                compositionNotes = composed.Message.Notes;
                break;

            // The notes are the loop's account of a message it is returning, and it is not
            // returning this one, so a refused draft has none. suppression_reason and the
            // queue row are what say what happened to it.
            case ComposeOutcome.Refused refused:
                log.LogWarning("Compose-validate loop refused its draft ({Error}); validating it here to record which checks it failed.", refused.Error);
                draft = refused.Draft;
                compositionNotes = null;
                break;

            default:
                var failed = (ComposeOutcome.Failed)composeOutcome;
                log.LogWarning("Suppressing message: composition failed ({Error}).", failed.Error);
                return Suppressed(prospectCase, consentDecision, SuppressionReason.CompositionFailed, nextAction, actionPlan);
        }

        // Step 4: schedule (A4, A5). The scheduler returns the send with its working, so the
        // diagnostics can name the floor, the zone and the slot the way they name the plan
        // (D22); the slot is never a wall time the zone did not reach (A20).
        ScheduledSend scheduled = scheduler.Resolve(referenceTime, context.LastInteraction, context.TimeZoneId, channel);
        NextMessage finalMessage = draft with { SendAt = scheduled.SendAt };
        var scheduleNotes = new ScheduleNotes(scheduled.Floor, scheduled.TimeZoneId, scheduled.Slot);

        // Step 5: validate. An unsafe or off-brand draft never leaves the agent (DESIGN.md
        // section 5): ValidatingMessageComposer already guarantees a clean message under
        // normal wiring, but this is the orchestrator's own gate, not borrowed trust in the
        // composer's cooperation.
        SafetyValidationResult validation = validator.Validate(finalMessage, prospectCase.ConstraintsOrEmpty);

        bool hasViolations = validation.Violations.Count > 0;

        // D38: the fair-housing state is the fair-housing check's own verdict, never
        // "no violations at all". A message that merely omitted its opt-out line used to
        // record a fair-housing failure that never happened, and A14 says a state is
        // earned by the step that proves it.
        bool fairHousingCheckPassed = validation.VerdictOf(SafetyCheck.FairHousing) == SafetyCheckVerdict.Passed;

        // D42's second part, and D39's classification of it: brand style is a diagnostic, so
        // it is checked here and never gates. A message that breaks a rule still goes out and
        // the state is recorded not earned, because an off-voice message is off-voice and not
        // unlawful. It is not in ValidatingMessageComposer's loop for the same reason.
        BrandStyleValidationResult brandStyle = BrandStyleValidator.Validate(finalMessage);

        // Playbook step 43's rule applied to this check: the diagnostics file carries the
        // failed rules too, but only when the run was given --diagnostics, and a reader of the
        // log should not have to guess which of the three rules the message broke.
        if (!brandStyle.Applied)
        {
            log.LogWarning("Brand style not applied: {FailedRules}.", string.Join(", ", brandStyle.FailedRules));
        }

        var diagnostics = new AgentDiagnostics(
            RequiredStateMap.For(
                prospectCase.Assertions?.RequiredStates,
                Verdict(consentDecision.ConsentVerified),
                Verdict(fairHousingCheckPassed),
                Verdict(brandStyle.Applied)),
            validation.Violations.Count,
            brandStyle.FailedRules,
            hasViolations ? SuppressionReason.SafetyViolation : SuppressionReason.None,
            actionPlan,
            scheduleNotes,
            compositionNotes);

        if (hasViolations)
        {
            log.LogWarning("Suppressing message: final safety validation found {ViolationCount} violation(s).", validation.Violations.Count);

            // Composition is null on a record that has no message (AgentDiagnostics.cs):
            // this record joins the other two suppression cases in having none, so it
            // joins them in nulling the field the compose step already wrote.
            //
            // D43: the draft goes out with the result rather than being dropped here. It
            // carries this validation's own violations, by check, so the one gate that
            // rejected the message is the one source of the reason it was rejected.
            return new AgentRunResult(
                new AgentOutput(SuppressedMessage(), nextAction),
                diagnostics with { Composition = null },
                new RejectedDraft(finalMessage, validation.ViolationsByCheck()));
        }

        // Step 6: emit.
        log.LogInformation("Message composed: channel={Channel}, nextAction={NextAction}.", channel, nextAction.Type);
        return new AgentRunResult(new AgentOutput(finalMessage, nextAction), diagnostics);
    }

    // D3: suppression on the wire is a next_message object with channel none and every
    // other member null, the oracle's own spelling, never a null object.
    private static NextMessage SuppressedMessage() => new(CommunicationChannel.None);

    // A14: a state is earned by the step that proves it. One helper for all three states, so
    // "the step ran and said no" is spelled the same way wherever it comes from.
    private static RequiredStateVerdict Verdict(bool earned) =>
        earned ? RequiredStateVerdict.Earned : RequiredStateVerdict.NotEarned;

    // The consent gate ran on every record that reaches here, so consent_verified is answered.
    // Neither the safety validator nor the brand-style validator did, because this record has
    // no message: not evaluated is the honest answer, and it is not a pass (A15).
    private static AgentRunResult Suppressed(
        ProspectCase prospectCase,
        ConsentDecision consentDecision,
        SuppressionReason reason,
        NextAction nextAction,
        ActionPlanNotes? actionPlan)
    {
        var diagnostics = new AgentDiagnostics(
            RequiredStateMap.For(
                prospectCase.Assertions?.RequiredStates,
                Verdict(consentDecision.ConsentVerified),
                RequiredStateVerdict.NotEvaluated,
                RequiredStateVerdict.NotEvaluated),
            SafetyViolationCount: 0,
            BrandStyleFailures: null,
            reason,
            actionPlan);

        return new AgentRunResult(new AgentOutput(SuppressedMessage(), nextAction), diagnostics);
    }
}
