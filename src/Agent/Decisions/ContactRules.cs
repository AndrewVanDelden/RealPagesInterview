using Agent.Common;
using Agent.Domain;

namespace Agent.Decisions;

// Records that are not sent a message whatever their consent says, checked after consent and before
// anything is planned. Some are sent nothing (no_op); some need a person's judgment first
// (escalate_to_human). Each returns the reason next_action carries, and the first rule that applies
// answers.
public static class ContactRules
{
    private static readonly string[] ServedPersonas = ["prospect", "resident", "guarantor"];

    // O(1): a fixed number of field reads and short string comparisons.
    public static Option<NextAction> Check(ProspectCase prospectCase, DateTimeOffset referenceTime, DateOnly referenceDate)
    {
        ProspectContext context = prospectCase.ContextOrEmpty;
        ProspectProfile profile = context.ProfileOrEmpty;

        // Any opt-out the profile records stops contact, even when the consent flags were not updated.
        if (profile.OptOutRequestedAt is not null)
        {
            return NoOp("opt_out_on_record");
        }

        // A minor cannot sign a lease, so a prospect under 18 is not marketed to.
        if (Personas.IsProspect(prospectCase.Persona) && profile.Age is < 18)
        {
            return NoOp("prospect_under_18");
        }

        // A persona the agent has no catalog for, such as a vendor, is not a leasing party to message.
        if (prospectCase.Persona is { } persona && !ServedPersonas.Contains(persona.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return NoOp("unsupported_persona");
        }

        // A resident in collections is subject to debt-collection rules, which a person handles.
        if (IsStage(prospectCase, "delinquent_collections"))
        {
            return Escalate("regulated_communication");
        }

        // A cancellation over a screening result is an adverse decision about the applicant, so a
        // person reviews it before any follow-up; a cancellation for another reason is not stopped.
        if (context.CancellationReason is { } cancellation && cancellation.Trim().StartsWith("screening", StringComparison.OrdinalIgnoreCase))
        {
            return Escalate("manager_cancellation_requires_review");
        }

        // A renewal message for a lease that has already ended cannot be right, so a person looks at it.
        if (prospectCase.LifecycleStage is { } stage && stage.Trim().StartsWith("renewal", StringComparison.OrdinalIgnoreCase)
            && context.LeaseEndDate is { } leaseEnd && leaseEnd < referenceDate)
        {
            return Escalate("lease_end_date_in_past");
        }

        // A tour that has not happened yet was not missed.
        if (IsStage(prospectCase, "no_show") && context.MissedTourTime is { } tourTime && tourTime > referenceTime)
        {
            return NoOp("missed_tour_time_in_future");
        }

        return Option<NextAction>.None();
    }

    private static bool IsStage(ProspectCase prospectCase, string stage) =>
        prospectCase.LifecycleStage is { } value && string.Equals(value.Trim(), stage, StringComparison.OrdinalIgnoreCase);

    private static Option<NextAction> NoOp(string reason) =>
        Option<NextAction>.Some(new NextAction(ActionTypes.NoOp, Reason: reason));

    private static Option<NextAction> Escalate(string reason) =>
        Option<NextAction>.Some(new NextAction(ActionTypes.EscalateToHuman, Reason: reason));
}
