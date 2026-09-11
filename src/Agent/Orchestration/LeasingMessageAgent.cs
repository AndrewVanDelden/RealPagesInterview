using Agent.Common;
using Agent.Composition;
using Agent.Decisions;
using Agent.Domain;
using Agent.Safety;
using Microsoft.Extensions.Logging;

namespace Agent.Orchestration;

// Holds no business rules of its own (DESIGN.md section 5): every decision is
// delegated to the component that owns it. `.Value` on Option<CommunicationChannel>
// at step 1 is read one line after the same option was tested for a value, so it is a
// guarded read and not a second answer to a question already asked: D57 merged the old
// consent gate into the selector precisely because two components were computing the
// same predicate. The compose outcome and the final safety validation are different:
// both are real, reachable failure modes (an unsalvageable compose-validate loop, or a
// violation slipping past composition), so both are handled explicitly below rather
// than trusted with .Value.
public sealed class LeasingMessageAgent(
    ChannelSelector channelSelector,
    IMessageComposer composer,
    ISafetyValidator validator,
    SendScheduler scheduler,
    NextActionPlanner planner,
    ILogger<LeasingMessageAgent>? logger = null)
{
    // A14: step 1, the consent-driven channel selection, is the step that owns this state, and
    // the state is earned by a record reaching that step at all. Step 1 runs on every record
    // that reaches the agent and nothing below revisits it, so the verdict is a constant and no
    // input makes it anything else: `channel_preferences: []` reaches step 1, the selector
    // returns no value there without reading consent once, and the record still records earned,
    // which is what the deleted consent gate recorded on that input too (D57).
    private const RequiredStateVerdict ConsentVerified = RequiredStateVerdict.Earned;

    private readonly ILogger<LeasingMessageAgent> log = logger.OrNullLogger();

    // D16: no log scope is opened here. The caller's batch loop (CliRunner) is the one
    // owner of the TaskId scope; a second one here rendered every line as
    // "TaskId=x TaskId=x". A library caller that wants correlation opens its own scope.
    //
    // referenceTime is the run's clock (D10): a value the caller passes, never read here.
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

        // Step 1: select the contactable channel, consent first (D2). No option value means
        // no preferred channel is consented, which is a no_op with its reason and nothing
        // else runs.
        Option<CommunicationChannel> contactableChannel = channelSelector.Select(prospectCase.ChannelPreferences, prospectCase.Consent);

        if (!contactableChannel.HasValue)
        {
            log.LogInformation("Suppressing message: prospect is not contactable.");
            return Suppressed(
                prospectCase,
                SuppressionReason.NoContactConsent,
                new NextAction(ActionTypes.NoOp, Reason: SuppressionReason.NoContactConsent.ToWireName()),
                actionPlan: null,
                modelCost: null,
                networkRetries: null);
        }

        CommunicationChannel channel = contactableChannel.Value;

        // Step 2: plan the next action from the horizon (A7), counted in the record's local
        // date (D10).
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

        // Step 3: compose. Three outcomes, and two of them carry a draft (D48): a composed
        // message, and one the compose-validate loop refused on safety. The refused draft is
        // scheduled and validated below exactly like a composed one, so step 5 is the one
        // place that names the violations; a composition that produced no draft at all is the
        // only one that short-circuits here, because there is nothing to validate.
        ComposeOutcome composeOutcome = await composer.ComposeAsync(prospectCase, channel, cancellationToken: cancellationToken);

        // D66: what the run spent is read once, here, and reaches every diagnostics this method
        // builds below. It is a fact about the record and not about a message, so unlike the
        // composition notes it survives an outcome that ships nothing.
        ModelCostNotes? modelCost = composeOutcome.ModelCost;
        int? networkRetries = composeOutcome.NetworkRetries;

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
                return Suppressed(prospectCase, SuppressionReason.CompositionFailed, nextAction, actionPlan, modelCost, networkRetries);
        }

        // Step 4: schedule (A4, A5). The scheduler returns the send with its working, so the
        // diagnostics can name the floor, the zone and the slot the way they name the plan
        // (D22); the slot is never a wall time the zone did not reach (A20).
        ScheduledSend scheduled = scheduler.Resolve(referenceTime, context.LastInteraction, context.TimeZoneId, channel, prospectCase.Persona, prospectCase.LifecycleStage);
        NextMessage finalMessage = draft with { SendAt = scheduled.SendAt };
        var scheduleNotes = new ScheduleNotes(scheduled.Floor, scheduled.TimeZoneId, scheduled.Slot, scheduled.Source);

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
                ConsentVerified,
                Verdict(fairHousingCheckPassed),
                Verdict(brandStyle.Applied)),
            validation.Violations.Count,
            brandStyle.FailedRules,
            hasViolations ? SuppressionReason.SafetyViolation : SuppressionReason.None,
            actionPlan,
            scheduleNotes,
            compositionNotes,
            modelCost,
            networkRetries);

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

    // A14 again, for the two states a step can answer either way. One helper for both, so
    // "the step ran and said no" is spelled the same way wherever it comes from.
    private static RequiredStateVerdict Verdict(bool earned) =>
        earned ? RequiredStateVerdict.Earned : RequiredStateVerdict.NotEarned;

    // Neither the safety validator nor the brand-style validator ran on a suppressed record,
    // because it has no message: not evaluated is the honest answer, and it is not a pass
    // (A15).
    // D66: what the record spent is a parameter rather than a constant here, because the two
    // callers are different facts. A record with no consented channel never reached a composer
    // and has nothing to report; a composition failure reached one, and its calls were billed
    // for whether or not anything came back.
    private static AgentRunResult Suppressed(
        ProspectCase prospectCase,
        SuppressionReason reason,
        NextAction nextAction,
        ActionPlanNotes? actionPlan,
        ModelCostNotes? modelCost,
        int? networkRetries)
    {
        var diagnostics = new AgentDiagnostics(
            RequiredStateMap.For(
                prospectCase.Assertions?.RequiredStates,
                ConsentVerified,
                RequiredStateVerdict.NotEvaluated,
                RequiredStateVerdict.NotEvaluated),
            SafetyViolationCount: 0,
            BrandStyleFailures: null,
            reason,
            actionPlan,
            Schedule: null,
            Composition: null,
            modelCost,
            networkRetries);

        return new AgentRunResult(new AgentOutput(SuppressedMessage(), nextAction), diagnostics);
    }
}
