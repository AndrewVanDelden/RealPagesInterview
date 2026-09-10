namespace Agent.Composition;

// What a completion client returns: the text, how many transport retries it took to get it
// (D28, playbook step 49), and what the vendor says the call cost in tokens (D62). Every count
// travels with the result rather than being read off the client afterwards, so the composer can
// put them in the diagnostics without knowing how the client is built.
//
// D62: tokens, never money. A price is a fact about a vendor's web page and not about this
// program, so no test in this suite could tell a stale constant from a current one; the dollar
// figure stays dated prose in DESIGN.md section 9 and README, and a reader who needs today's
// money multiplies today's price by these counts. Zero is what a completed call whose response
// carried no usage block measured, which is why ModelCostNotes counts the call beside the
// tokens rather than letting zero tokens read as free.
public sealed record ModelCompletion(string Content, int NetworkRetries, int InputTokens = 0, int OutputTokens = 0);
