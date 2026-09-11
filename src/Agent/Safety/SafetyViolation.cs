namespace Agent.Safety;

// One violation line, attached to the check that produced it, for the review queue. The flat
// IReadOnlyList<string> on SafetyValidationResult stays what the compose-validate loop feeds
// back to the next attempt and what the agent counts; this is the same lines for a reader who
// has to act on them, where "which check" is the first thing the reader needs and a string
// that begins "Body contains..." makes them guess it.
public sealed record SafetyViolation(SafetyCheck Check, string Detail);
