using Agent.Domain;
using Agent.Safety;

namespace Agent.Orchestration;

// The draft the safety gate rejected, and every check that rejected it. It rides on
// AgentRunResult rather than on AgentDiagnostics because it is not diagnostics: the
// diagnostics file is a per-record dump of how every decision was reached and is read when
// debugging a run, and this is a work list a person acts on, written to its own output.
//
// It is null on every record that is not a safety suppression, which is a message that went
// out, a record with no consented channel, and a composition that produced no draft.
public sealed record RejectedDraft(NextMessage Message, IReadOnlyList<SafetyViolation> Violations);
