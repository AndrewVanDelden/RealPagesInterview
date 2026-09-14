using System.Globalization;

namespace Agent.Composition;

// What a record's model calls cost, in tokens and never in money: a price constant is a fact
// about a vendor's web page, so no test could tell a stale one from a current one, and the
// dollar figure stays dated prose in DESIGN.md section 9 and README. Three states, never one
// zero: no model call (the template composer) is a null record; a call abandoned at its timeout
// counts in Calls but not CompletedCalls, with zero tokens, because the client throws before
// it can read what the vendor still billed; a completed call has the vendor's own counts.
// Calls counts CompleteAsync calls, one per compose attempt that reached the model; transport
// retries inside a call are ComposeOutcome.NetworkRetries and are not counted again here.
public sealed record ModelCostNotes(int Calls, int CompletedCalls, int InputTokens, int OutputTokens)
{
    // Null plus anything is that thing: an attempt that made no model call adds no measurement,
    // so a record whose every attempt stayed offline still reports null rather than a zero
    // nobody measured. O(1).
    public static ModelCostNotes? Add(ModelCostNotes? left, ModelCostNotes? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return new ModelCostNotes(
            left.Calls + right.Calls,
            left.CompletedCalls + right.CompletedCalls,
            left.InputTokens + right.InputTokens,
            left.OutputTokens + right.OutputTokens);
    }

    // What a failed completion call costs, the one rule every ICompletionClient caller shares
    // (OpenAiMessageComposer.ComposeAsync, SemanticJudge.GradeAsync): a completion with no
    // choice still carries the vendor's own usage block on the exception, so it is counted as
    // completed with those tokens; any other failure is a counted call the client threw before
    // or instead of a completion, so nothing came back to measure. O(1).
    public static ModelCostNotes ForFailedCall(Exception ex) => ex is NoCompletionChoiceException noChoice
        ? new ModelCostNotes(Calls: 1, CompletedCalls: 1, noChoice.InputTokens, noChoice.OutputTokens)
        : new ModelCostNotes(Calls: 1, CompletedCalls: 0, InputTokens: 0, OutputTokens: 0);

    // One rendering for both per-batch surfaces, the scorecard and the Batch complete log line,
    // so the two cannot word the same number differently. O(1).
    public static string Describe(ModelCostNotes? notes) =>
        notes is null
            ? "none"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{notes.Calls} call(s), {notes.CompletedCalls} completed, {notes.InputTokens} input + {notes.OutputTokens} output token(s)");
}
