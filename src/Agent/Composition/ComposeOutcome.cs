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
