using Agent.Domain;
using Agent.Safety;

namespace Agent.Composition;

// What the composition seam returns. Three cases, because a refusal and a failure are
// different facts: a refusal has a draft a person can read and decide about, a failure has
// none. A Result could carry only a message or an error string, which destroys a refused draft
// at the seam, so it could not be queued for review and the record would read as a composition
// failure. Refused carries the draft and not the violations: the orchestrator re-derives those
// with its own step 5 validator, so one fact never has two sources. Result<T> stays everywhere
// else, because nowhere else has a third state.
public abstract record ComposeOutcome
{
    // Private, so the three cases below are the whole hierarchy and a caller that has handled
    // them has handled every outcome.
    private ComposeOutcome()
    {
    }

    // What this record's run spent, on the base type so all three cases answer for it:
    // ModelCost is the vendor's token counts and NetworkRetries the transport retries inside
    // the calls. They are not on CompositionNotes because the notes describe a returned message,
    // and a refused draft and a composition failure return none, yet the vendor billed for the
    // calls behind them. Null on either is the absence of a measurement, never a measured zero:
    // a record that made no model call reports null, not a zero nobody measured.
    public ModelCostNotes? ModelCost { get; init; }

    public int? NetworkRetries { get; init; }

    // A message to send. Validation is the verdict the compose-validate loop reached on this
    // message, and only the loop sets it: every other composer leaves it null, and the agent's
    // final gate validates any message that arrives without a verdict it can use.
    public sealed record Composed(ComposedMessage Message) : ComposeOutcome
    {
        public DraftValidation? Validation { get; internal init; }
    }

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
