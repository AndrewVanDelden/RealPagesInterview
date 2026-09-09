using Agent.Domain;
using Agent.Safety;

namespace Agent.Orchestration;

// One row of the review queue (D43): a record the safety gate suppressed, the checks that
// suppressed it, and the draft a person has to decide about. The channel is not a member of
// its own; it is on the draft, and two spellings of one fact is what a `with` copy
// desynchronizes.
//
// This file carries prospect text by design, which is the one place in this program that is
// true: it is a file a reviewer opens, not a log line, and step 68's redaction rule is about
// logs.
public sealed record ReviewQueueEntry(string TaskId, IReadOnlyList<SafetyViolation> Violations, NextMessage Draft);
