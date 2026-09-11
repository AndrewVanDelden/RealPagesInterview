namespace Agent.Composition;

// What a completion client returns: the text, how many transport retries it took (playbook
// step 49), and what the vendor says the call cost in tokens. Every count travels with the
// result rather than being read off the client afterwards, so the composer can put them in the
// diagnostics without knowing how the client is built. Tokens, never money: a price is a fact
// about a vendor's web page, so no test could tell a stale constant from a current one, and the
// dollar figure stays dated prose in DESIGN.md section 9 and README. Zero tokens is what a
// completed call with no usage block measured, which is why ModelCostNotes counts the call too.
public sealed record ModelCompletion(string Content, int NetworkRetries, int InputTokens = 0, int OutputTokens = 0);
