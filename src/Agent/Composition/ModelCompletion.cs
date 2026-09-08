namespace Agent.Composition;

// What a completion client returns: the text, and how many transport retries it took to get
// it (D28, playbook step 49). The count travels with the result rather than being read off
// the client afterwards, so the composer can put it in the diagnostics without knowing how
// the client is built.
public sealed record ModelCompletion(string Content, int NetworkRetries);
