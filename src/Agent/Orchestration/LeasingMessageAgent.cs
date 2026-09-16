using Agent.Common;
using Agent.Composition;
using Agent.Decisions;
using Agent.Domain;
using Agent.Ingest;
using Agent.Safety;
using Microsoft.Extensions.Logging;

namespace Agent.Orchestration;

// Holds no business rules of its own (DESIGN.md section 5): every decision is delegated to
// the component that owns it. `.Value` on Option<CommunicationChannel> at step 1 is read one
// line after the option was tested, a guarded read: the selector's absence of a value is the
// one answer to "contactable", so no second component computes the same predicate. The
// compose outcome and the final safety validation are different:
// both are real, reachable failure modes (an unsalvageable compose-validate loop, or a
// violation slipping past composition), so both are handled explicitly below rather
// than trusted with .Value.
public sealed class LeasingMessageAgent(
    ChannelSelector channelSelector,
    IMessageComposer composer,
    ISafetyValidator validator,
    SendScheduler scheduler,
    NextActionPlanner planner,
    ILogger<LeasingMessageAgent>? logger = null,
    PropertyData? propertyData = null)
{
    // A14: step 1, the consent-driven channel selection, is the step that owns this state, and
    // the state is earned by a record reaching that step at all. Step 1 runs on every record
    // that reaches the agent and nothing below revisits it, so the verdict is a constant and no
    // input makes it anything else: `channel_preferences: []` reaches step 1, the selector
    // returns no value there without reading consent once, and the record still records earned.
    private const RequiredStateVerdict ConsentVerified = RequiredStateVerdict.Earned;

    private readonly ILogger<LeasingMessageAgent> log = logger.OrNullLogger();

    // No log scope is opened here. The caller's batch loop (CliRunner) is the one
    // owner of the TaskId scope; a second one here rendered every line as
    // "TaskId=x TaskId=x". A library caller that wants correlation opens its own scope.
    //
    // referenceTime is the run's clock: a value the caller passes, never read here.
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
        // Every input field is cleaned before anything reads it, whoever called the agent: the same
        // cleaning CliRunner applies, and cleaning an already cleaned record changes nothing.
        ProspectCase sanitized = InputSanitizer.Sanitize(prospectCase).Case;
        try
        {
            return await RunUnguardedAsync(sanitized, referenceTime, cancellationToken);
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

        // Step 1: select the contactable channel, consent first. No option value means
        // no preferred channel is consented, which is a no_op with its reason and nothing
        // else runs.
        Option<CommunicationChannel> contactableChannel = channelSelector.Select(prospectCase.ChannelPreferences, prospectCase.ConsentOrEmpty);

        if (!contactableChannel.HasValue)
        {
            log.LogInformation("Suppressing message: prospect is not contactable.");
            return Suppressed(
                prospectCase,
                SuppressionReason.NoContactConsent,
                new NextAction(ActionTypes.NoOp, Reason: SuppressionReason.NoContactConsent.ToWireName()),
                actionPlan: null,
                modelCost: null,
                networkRetries: null,
                renewalOfferLoaded: RequiredStateVerdict.NotEvaluated);
        }

        CommunicationChannel channel = contactableChannel.Value;

        // A6: the zone every date below is counted in, the record's own id or, when the runtime does not
        // know it, the zone of the state its city_interest names.
        string? timeZoneId = TimeZones.EffectiveZoneId(context.TimeZoneId, context.ProfileOrEmpty.City);
        DateOnly referenceDate = TimeZones.ToLocalDate(referenceTime, timeZoneId);

        // Step 1b: a record some rule says must not be messaged is answered here, before anything is
        // planned or composed: sent nothing, or handed to a person.
        Option<NextAction> ruled = ContactRules.Check(prospectCase, referenceTime, referenceDate);
        if (ruled.HasValue)
        {
            bool escalated = ruled.Value.Type == ActionTypes.EscalateToHuman;
            log.LogInformation("Suppressing message: contact rule {Reason}.", ruled.Value.Reason);
            return Suppressed(
                prospectCase,
                escalated ? SuppressionReason.EscalatedToHuman : SuppressionReason.DoNotContact,
                ruled.Value,
                actionPlan: null,
                modelCost: null,
                networkRetries: null,
                renewalOfferLoaded: RequiredStateVerdict.NotEvaluated);
        }

        // renewal_offer_loaded is earned by finding the record's renewal offer in the property
        // data, the stand-in for the property management system, and not earned when the run has
        // no property data or it holds no offer for this record.
        Option<PropertyFacts> propertyFacts = propertyData is null ? Option<PropertyFacts>.None() : propertyData.FactsFor(context.PropertyName);
        RequiredStateVerdict renewalOfferLoaded = Verdict(
            propertyFacts.HasValue && propertyFacts.Value.RenewalOfferFor(context.Unit, context.RenewalOfferId).HasValue);

        // Step 2: plan the next action from the horizon (A7), counted in days from the run's
        // reference time as a date in the record's own zone.
        PlannedAction planned = planner.Plan(
            prospectCase.Persona,
            prospectCase.LifecycleStage,
            context.MoveDateTarget,
            referenceDate,
            callToActionStated: !Presence.IsAbsent(prospectCase.ConstraintsOrEmpty.PrimaryCta));
        NextAction nextAction = planned.Action;
        var actionPlan = new ActionPlanNotes(planned.Branch, planned.HorizonDays, planned.Source);

        // Playbook step 43: the catalog's fallback is defined, and firing it is recorded
        // where a reader will see it. The diagnostics file carries the same fact, but only
        // when the run was given --diagnostics.
        //
        // Warning, not Information: no catalog row stated this action, so the record is also
        // queued for a person to review. The line names the source and the branch only. The
        // persona and stage are the record's own free text, and on a missed match they are by
        // definition not catalog keys, so they stay out of the log; the review queue names them.
        if (planned.Source != ActionSource.CatalogRow)
        {
            log.LogWarning(
                "Next action came from the generic row ({Source}), branch={Branch}; the review queue names the persona and stage.",
                planned.Source,
                planned.Branch);
        }

        // A planned no_op is a record that must not be contacted, a closed lead or a persona and stage
        // that contradict each other, so nothing is composed or sent and the action's reason says why.
        if (nextAction.Type == ActionTypes.NoOp)
        {
            log.LogInformation("Suppressing message: the planned action is no_op.");
            return Suppressed(prospectCase, SuppressionReason.NoOpAction, nextAction, actionPlan, modelCost: null, networkRetries: null, renewalOfferLoaded);
        }

        // Step 3: schedule (A4, A5), before composing, because a tour invitation's reply options are
        // the tour slots counted from the send time. The scheduler reads the record and the channel and
        // nothing a draft sets. It returns the send with its working, so the diagnostics can name the
        // floor, the zone and the slot the way they name the plan; the slot is never a wall time the
        // zone did not reach (A20). The tour slots follow the property's calendar when the property
        // data holds one.
        ScheduledSend scheduled = scheduler.Resolve(referenceTime, context.LastInteraction, timeZoneId, channel, prospectCase.Persona, prospectCase.LifecycleStage, planned.Branch);
        IReadOnlyList<DateTimeOffset> tourSlots = TourSlots.For(
            referenceTime,
            scheduled.SendAt,
            timeZoneId,
            propertyFacts.HasValue ? propertyFacts.Value.TourCalendar : null);

        // Step 4: compose. Three outcomes, and two of them carry a draft: a composed
        // message, and one the compose-validate loop refused on safety. The refused draft is
        // validated below like any draft without the loop's verdict, so step 5 is the one
        // place that names the violations; a composition that produced no draft at all is the
        // only one that short-circuits here, because there is nothing to validate.
        ComposeOutcome composeOutcome = await composer.ComposeAsync(prospectCase, channel, cancellationToken: cancellationToken, tourSlots: tourSlots, referenceDate: referenceDate);

        // What the run spent is read once, here, and reaches every diagnostics this method
        // builds below. It is a fact about the record and not about a message, so unlike the
        // composition notes it survives an outcome that ships nothing.
        ModelCostNotes? modelCost = composeOutcome.ModelCost;
        int? networkRetries = composeOutcome.NetworkRetries;

        NextMessage draft;
        CompositionNotes? compositionNotes;
        DraftValidation? loopValidation;

        switch (composeOutcome)
        {
            case ComposeOutcome.Composed composed:
                draft = composed.Message.Message;
                compositionNotes = composed.Message.Notes;
                loopValidation = composed.Validation;
                break;

            // The notes are the loop's account of a message it is returning, and it is not
            // returning this one, so a refused draft has none. suppression_reason and the
            // queue row are what say what happened to it.
            case ComposeOutcome.Refused refused:
                log.LogWarning("Compose-validate loop refused its draft ({Error}); validating it here to record which checks it failed.", refused.Error);
                draft = refused.Draft;
                compositionNotes = null;
                loopValidation = null;
                break;

            default:
                var failed = (ComposeOutcome.Failed)composeOutcome;
                log.LogWarning("Suppressing message: composition failed ({Error}).", failed.Error);
                return Suppressed(prospectCase, SuppressionReason.CompositionFailed, nextAction, actionPlan, modelCost, networkRetries, renewalOfferLoaded);
        }

        // The draft takes the send time step 3 resolved.
        NextMessage finalMessage = draft with { SendAt = scheduled.SendAt };
        var scheduleNotes = new ScheduleNotes(scheduled.Floor, scheduled.TimeZoneId, scheduled.Slot, scheduled.Source);

        // Step 5: validate. An unsafe or off-brand draft never leaves the agent (DESIGN.md
        // section 5). The loop's verdict is used only when it answers the question this gate
        // would ask: this gate's own validator, this draft, this record's constraints. The loop
        // judged the draft before the send time was set, and the validator reads only the
        // subject, the body and the constraints, so the send time cannot change the verdict.
        // Every other draft, a refused one or one from a composer that is not the loop, is
        // validated here, so no composer's own word that its message is clean is ever taken.
        CaseConstraints constraints = prospectCase.ConstraintsOrEmpty;
        Option<SafetyValidationResult> loopVerdict = loopValidation is null
            ? Option<SafetyValidationResult>.None()
            : loopValidation.ResultFor(validator, draft, constraints);
        SafetyValidationResult validation = loopVerdict.HasValue ? loopVerdict.Value : validator.Validate(finalMessage, constraints);

        bool hasViolations = validation.Violations.Count > 0;

        // The fair-housing state is the fair-housing check's own verdict, never "no
        // violations at all": a message that merely omitted its opt-out line would otherwise
        // record a fair-housing failure that never happened, and A14 says a state is
        // earned by the step that proves it.
        bool fairHousingCheckPassed = validation.VerdictOf(SafetyCheck.FairHousing) == SafetyCheckVerdict.Passed;

        // Brand style is a diagnostic, so
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
                Verdict(brandStyle.Applied),
                renewalOfferLoaded),
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
            // The draft goes out with the result for the review queue rather than being
            // dropped here: a suppression is a business decision a person has to see. It
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

    // Suppression on the wire is a next_message object with channel none and every
    // other member null, the oracle's own spelling, never a null object, so the output
    // always has both members.
    private static NextMessage SuppressedMessage() => new(CommunicationChannel.None);

    // A14 again, for the two states a step can answer either way. One helper for both, so
    // "the step ran and said no" is spelled the same way wherever it comes from.
    private static RequiredStateVerdict Verdict(bool earned) =>
        earned ? RequiredStateVerdict.Earned : RequiredStateVerdict.NotEarned;

    // Neither the safety validator nor the brand-style validator ran on a suppressed record,
    // because it has no message: not evaluated is the honest answer, and it is not a pass
    // (A15).
    // What the record spent is a parameter rather than a constant here, because the two
    // callers are different facts. A record with no consented channel never reached a composer
    // and has nothing to report; a composition failure reached one, and its calls were billed
    // for whether or not anything came back.
    private static AgentRunResult Suppressed(
        ProspectCase prospectCase,
        SuppressionReason reason,
        NextAction nextAction,
        ActionPlanNotes? actionPlan,
        ModelCostNotes? modelCost,
        int? networkRetries,
        RequiredStateVerdict renewalOfferLoaded)
    {
        var diagnostics = new AgentDiagnostics(
            RequiredStateMap.For(
                prospectCase.Assertions?.RequiredStates,
                ConsentVerified,
                RequiredStateVerdict.NotEvaluated,
                RequiredStateVerdict.NotEvaluated,
                renewalOfferLoaded),
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
