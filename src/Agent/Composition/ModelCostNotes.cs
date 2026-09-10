using System.Globalization;

namespace Agent.Composition;

// D62: what a record's model calls cost, in tokens and never in money. A price constant is a
// fact about a vendor's web page rather than about this program, so no test in this suite could
// tell a stale one from a current one; the dollar figure stays dated prose in DESIGN.md section
// 9 and README, and a reader who needs today's money multiplies today's price by these counts.
//
// Four counts, because the three states a record can be in are three different facts and must
// not collapse into one zero. No model call at all (the template composer): the whole record is
// null, which is the rule CompositionNotes.NetworkRetries already states. A call abandoned at
// its timeout: Calls counts it, CompletedCalls does not, and both token counts are zero,
// because OpenAiCompletionClient throws before it can read result.Value and therefore cannot
// see what the vendor still billed. A completed call: the vendor's own input and output counts.
// Calls minus CompletedCalls is how many attempts were abandoned, and it is what keeps zero
// tokens from reading as free.
//
// Calls counts calls to ICompletionClient.CompleteAsync, one per compose attempt that reached
// the model. The transport retries inside one such call are CompositionNotes.NetworkRetries and
// are not counted a second time here (D28).
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

    // One rendering for both per-batch surfaces, the scorecard and the Batch complete log line,
    // so the two cannot word the same number differently. O(1).
    public static string Describe(ModelCostNotes? notes) =>
        notes is null
            ? "none"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{notes.Calls} call(s), {notes.CompletedCalls} completed, {notes.InputTokens} input + {notes.OutputTokens} output token(s)");
}
