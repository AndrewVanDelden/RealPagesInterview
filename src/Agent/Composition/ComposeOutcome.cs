using Agent.Domain;

namespace Agent.Composition;

// What the composition seam returns (D48). Result<ComposedMessage> could say "a message" or
// "an error string" and nothing else, so a draft the compose-validate loop refused on safety
// was destroyed at the seam: no caller could queue it, and the record reached the
// orchestrator as a composition failure, which is a different fact and produced a different
// suppression reason and a fair-housing state of not evaluated.
//
// Three cases, because a refusal and a failure are different facts. A refusal has a draft a
// person can read and decide about; a failure has none. Refused carries the draft and not the
// violations: the orchestrator re-derives those with its own validator, which is the step 5
// gate it already had, so one fact never has two sources.
//
// This type replaces Result<ComposedMessage> on this seam only. Result<T> stays everywhere
// else it is used, because nowhere else has a third state.
public abstract record ComposeOutcome
{
    // Private, so the three cases below are the whole hierarchy and a caller that has handled
    // them has handled every outcome.
    private ComposeOutcome()
    {
    }

    // What this record's run spent, on the base type so all three cases answer for it (D66).
    // ModelCost is the token counts of D62 and NetworkRetries the transport retries of D28.
    // They are here rather than on CompositionNotes because the notes are an account of a
    // message that is being returned, and two of the three cases return none: a refused draft
    // and a composition failure both leave with no notes at all, which is where the counts
    // used to be dropped even though the vendor had already billed for the calls behind them.
    // Null on both is the absence of a measurement and never a measured zero, unchanged from
    // D62 and D28.
    public ModelCostNotes? ModelCost { get; init; }

    public int? NetworkRetries { get; init; }

    // A message to send.
    public sealed record Composed(ComposedMessage Message) : ComposeOutcome;

    // The two outcomes that carry no message to send, and a reason instead. One case for a
    // caller that needs the reason and not which of the two produced it: the compose-validate
    // loop is that caller, and reading them as one is what keeps it free of a branch for an
    // inner composer that refuses, which no composer in this program does.
    public abstract record NoMessage(string Error) : ComposeOutcome;

    // No draft exists: a transport failure, a malformed completion, a composer that could not
    // build one.
    public sealed record Failed(string Error) : NoMessage(Error);

    // A draft exists and the compose-validate loop refused it on safety. Nothing unsafe ships:
    // this is not a message to send, it is a message to review.
    public sealed record Refused(NextMessage Draft, string Error) : NoMessage(Error);
}
