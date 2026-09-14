using Agent.Common;
using Agent.Domain;

namespace Agent.Decisions;

// One row of the action catalog, keyed on the record's persona and lifecycle stage.
// Each horizon branch is an Option because the labeled records show one or two branches per row
// and A19 forbids inventing the others: None means "no evidence for this branch", and the generic
// row answers it. That is a different fact from "no row for this persona and stage", and
// ActionSource keeps the two apart in the diagnostics.
public sealed record ActionCatalogRow(
    string Persona,
    string LifecycleStage,
    Option<NextAction> ShortHorizonAction,
    Option<NextAction> LongHorizonAction,
    Option<NextAction> NoMoveDateAction)
{
    public Option<NextAction> ActionFor(HorizonBranch branch) => branch switch
    {
        HorizonBranch.Short => ShortHorizonAction,
        HorizonBranch.Long => LongHorizonAction,
        _ => NoMoveDateAction,
    };
}

// The fallback of playbook step 43 and A8. Both branches are defined, with no Option: that
// is what makes this the row every unanswered lookup can land on, so "the catalog has no
// answer" is not a state the program can reach. No record showed it a no-move-date value, so
// that branch is answered with the long action, which is what an absent date meant before the
// branch existed.
public sealed record GenericActionRow(NextAction ShortHorizonAction, NextAction LongHorizonAction)
{
    public NextAction ActionFor(HorizonBranch branch) =>
        branch == HorizonBranch.Short ? ShortHorizonAction : LongHorizonAction;
}
