# Decision log

The current phase, then one paragraph per sprint. Every decision paragraph is in
[DECISIONS_ARCHIVE.md](DECISIONS_ARCHIVE.md) in full, in the recording form of
`~/.agent-rules/ARCHITECTURE.md`: the question, the options, the recommendation, what it scopes,
the evidence, the numbered assumption it depends on (assumptions are the table in
[DESIGN.md](DESIGN.md) section 7). A citation of the form D or S followed by a number resolves
there by searching for its bold heading, which is the form every paragraph in it keeps; the
sprint paragraphs below name ranges of those numbers and define none. A task that cannot cite a
paragraph there is not scheduled. A new decision, bug or run and debug fact is written to the
archive in full and named in this file's paragraph for its sprint, which is at most 120 words
(D68).

## Current phase

Phase 7 of `~/.agent-rules/PROJECT_PLAYBOOK.md`: Test the way it will be judged.
Check: the scorecard on the synthetic set, the variance report and the fault-injection results
are all files in the repo. Status: not passed. `docs/FAULT_INJECTION.md` is there; the other two
are not, and the scorecard is written today only to a path `.gitignore` excludes, which is an
open question of its own before any of the three can be a file in the repo.
Phase 6 passed 2026-09-09 in Sprint 9, against the playbook's check and not this log's (D60):
the documented one-line command produced the output file, the diagnostics file, the review queue
and the scorecard on all three sets and exited 0, 0 and 2, checked by hand and recorded in
DESIGN.md section 9 (D63). The narration this block used to gate Phase 6 on is step 98, inside
Phase 8, and is still owed there.
Next step: decide where a committed scorecard and a variance report live, then produce them.
Open decisions: three. D31's question on how a real model-versus-template comparison could be
run at all and whether D32 to D37 get a sprint of their own need the requester; D67 is the third.

Replace these lines at the end of every sprint. Never append to them. A phase that passed, the
proof that passed it, the tallies it moved and any exception taken belong in the paragraphs
below and in DESIGN.md section 9, which is where the phase record lives.

## Decisions by sprint

**Starting decisions, S1 to S4 (2026-09-07).** The four defaults taken before the first sprint:
a library plus a thin command-line entry point that holds no rules (S1); code owns every
reproducible decision and the model writes prose and picks from a code-owned catalog (S2); one
seam per external or non-deterministic dependency, each with a real and an offline
implementation on the day it is created, and every other interface goes as the sprint that
touches it lands (S3); and an evaluator built before the product and proven able to fail (S4).

**D1 to D31, Sprints 1 to 6 (2026-09-07 to 2026-09-08).** The contracts: the input contract,
three required members and a default for the rest (D1); the action catalog and the planner (D2,
D17 to D20); the output and diagnostics contracts (D3, D22 to D24); scheduling and the
transition-day slot (D4, D21); composition, language and the call-to-action payload (D5, D25,
D26, D29); the evaluation contract, the scorer's label
corrections and the judge (D6, D13, D15, D30); the twelve-record file (D9, D11); gates, replay,
reference time and log scope (D8, D10, D14, D16); the official SDK and what bounds a model call
(D27, D28); and the step 60 run (D31). Structure is D7, the Phase 0 restart D12. Eleven addenda
amend them.

**D32 to D37, latency, proposed 2026-09-08.** Written from the step 60 run's measurements rather
than from preference: measure one successful call before tuning anything (D32), stop retrying a
timeout (D33), retry only a safety rejection in the compose-validate loop (D34), what one
attempt may take (D35), what `p95_latency_ms` is (D36), and batch concurrency (D37). D35 and D36
take no recommendation, and prompt caching is recorded there as not a lever. Their scheduling
call is still not taken, so a task citing any of them is not scheduled until it is.

**D38 to D56, Sprint 7, safety and states (2026-09-09).** One named result per safety check
rather than one boolean (D38), all four checks hard gates and brand style a diagnostic (D39),
which checks a record cannot switch off (D40), what the term proxy normalizes and exempts (D41,
D45, D49, D50), what earns a state and what brand style is (D42), the review queue for a
suppressed draft (D43), the vendor's retention default (D44), what an exception may say (D46,
D51), a null inside a list the element type forbids (D47), and the composer seam carrying a
refusal rather than destroying the draft (D48). D52 to D56 are the PR #24 review's fixes and
three of its findings reconsidered and kept.

**D57 to D59, Sprint 8, structure and narration (2026-09-09).** The consent gate merged into the
channel selector, whose absence of a value answers both questions (D57); every interface without
a second implementation deleted, leaving three seams (D58); and the design diagram renumbered to
the executed order rather than the code reordered to the diagram (D59). Three run and debug
facts sit with them: the sprint's own comment citations corrected in place, four review findings
on its prose, and the one baseline test D58 forced a token change in.

**D60 to D67, Sprint 9, the Phase 6 remainder and fault injection (2026-09-09).** Phase 6's
check is the playbook's, not this log's (D60); what latency and cost in the diagnostics mean
(D61, D62); step 81 is the Phase 6 check run rather than a second scoring pass (D63); the
fault-injection audit, five faults already proved and the output paths guarded (D64); the input
paths given the same guard, with empty arguments closed in parsing (D65); and the model spend
that vanished on exactly the records that failed (D66). D67 is open. A run and debug fact of
2026-09-10 records five PR #26 review findings fixed.

**D68, Sprint 10, the decision log trim (2026-09-10).** How this file gets under playbook step
92's two-thousand-word cap without breaking the citations that point into it from `src/`,
`tests/` and `docs/`: every full paragraph moves to `DECISIONS_ARCHIVE.md`, which keeps the bold
heading a citation resolves by, one paragraph per sprint stays here, and the word cap plus a
check that no cited number dangles move out of prose into `check-instruction-files.ps1`, which
CI runs. Executed 2026-09-10. A run and debug fact of the same day records seven review findings
fixed: what the check counts as a definition, the S numbers and the archive it did not scan, and
four lines of stale prose.

**D69 to D71, Sprint 11, the Phase 7 evidence (2026-09-10).** Where the committed scorecard
lives: written by the CLI into `docs/scorecards/`, a dated snapshot rather than a golden, because
two offline runs differed only on wall clock (D69). What the variance report measures: the
requester chose getting the model to answer at all, so a `--model-call-budget-ms` flag for
evaluation runs, after a scratch run measured 2 to 4 seconds a call against D28's 1000 ms
attempt (D70). A line that did not parse, or a record that threw, is an `ERROR` row that counts
in `Overall`, so the synthetic set reads 12 of 13 (D71).
