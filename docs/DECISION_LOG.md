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

Phase 8 of `~/.agent-rules/PROJECT_PLAYBOOK.md`: Documentation and review, reached 2026-09-10 after D82's flag merged and Phase 6 held (Phase 7 unchanged but for D88's first half).
Check: the README's run command and the runbook's steps both work from the tagged commit. Not yet run: the D88 pin and the release tag are open.
Next step: pin the D88 threshold on a quiet tree, run the four sets and the runbook from a fresh clone, then the sprint PR and the tag.
Open decisions: D89, warnings as errors in the build; the D86 check amendment; both proposed 2026-09-10.

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
take no recommendation, and prompt caching is recorded there as not a lever. All six were
settled in Sprint 11: D32 measured, D33 to D35 and D37 taken, D36 closed by D70.

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

**D69 to D73, Sprint 11, the Phase 7 evidence (2026-09-10).** The scorecard is committed under
`docs/scorecards/` (D69); `--model-call-budget-ms` lets the model answer on evaluation runs
(D70); a line that did not parse or a record that threw is an `ERROR` row, so the synthetic set
reads 12 of 13 (D71). Three live runs make `docs/VARIANCE.md` and pass Phase 7; code then took
the opt-out sentence and an unstated call to action (D72, D73), and refusals fell from 33 to 0.
The sprint also took D67, D34, D33, D35 and D37, did steps 86 to 88, and fixed three PR #28
review findings, a run and debug fact.

**D74 to D76, Sprint 12, Phase 8 documentation (2026-09-10).** The README meets step 90 and
every command in it ran; DESIGN.md has step 91's interface table, a decision number on every
rule, nine open questions, and Phase 7's changes to seven assumptions plus A22 and A23. Step 95
found no secret and no personal path; the repository stays public with the assignment's text and
data, the owner's choice (D74). Step 92's two fixes are made; D75 takes none of the step 96
changes. Four agents ran these steps at once, a Phase First departure on record. Step 93 reviewed
this sprint's diff, the one no PR had reviewed (D76), and found nothing; the second reviewer is
owed.

**D77 and D78, Sprint 13, the release (2026-09-10).** The owner marked step 93 complete with
its second reviewer not run. The annotated tag `v1.0.0` is on `ccf3c5c`; step 97's narrative is
`docs/RELEASE_v1.0.0.md`, which names the nine files step 94 has the owner read by hand.
`docs/NARRATION.md` opens with a 150-word script, one minute spoken, answering all seven
questions. The Phase 8 check passed from a worktree at the tag, a run and debug fact. D77 asks
whether the retrospective of 2026-09-06 already meets Phase 9; D78 asks where the narrative is
published, since `dev` shares no history with `main`. Both are open.

**D79, Sprint 14, Phase 9 (2026-09-10).** D77 and D78 were taken as (a): Phase 9 rests on the
retrospective of 2026-09-06, and the release is published on GitHub from
`docs/RELEASE_v1.0.0.md`. The owner read the nine step 94 files and delivered the narration,
closing Phase 8, a run and debug fact. The retrospective's new section 10 names the playbook step
for each of its sixteen findings: fourteen existing steps, one new step for finding 7, and finding
8 declined by D55. A step 104 audit of the failures since then found seven with no step and four
steps that earned nothing here; D79 took none of them, and the rules stay as they are.

**D80, cleanup (2026-09-10).** `TalkingPoints.md`, the Sprint 8 spoken script, deleted as a
duplicate of `docs/NARRATION.md` (D80). The same day the narration was checked against a trace of
one record through the code and a re-run of all three sets at the tag, a run and debug fact: eight
statements that did not match were fixed in place.

**D81 to D88, the architecture plan (proposed 2026-09-10).** A score of the system, a run and debug
fact, read 7 of 10 and traced the eight hold-out misses to their fields. The plan: the hold-out
becomes training data and a new frozen set carries the honest number (D81); the rules it shows,
compiled by default and loadable from a file (D82); a generic-row answer queued for review (D83);
one validation per composed record (D84); a scorecard that cannot carry stale tallies (D85);
bounded memory in the batch with a benchmark (D86); comments that state the rule and cite no
number (D87); a mutation score beside coverage (D88). None taken.

**D81 to D88 taken, Sprint 15 (2026-09-10).** The owner took every recommendation. The project
returns to Phase 0, since step 9 is the frozen set, and decisions merge into the sprint branch in
phase order while their work is written in parallel, one worktree each. D83 moves after D86, and
D88 pins its threshold after D87. Wave 1 edits `src` and `tests` on unmerged branches while
the phase reads 0, a deliberate exception held at the merge, and the D85 review found and fixed
a list the scorecard kept by reference, both a run and debug fact. D89 proposes warnings as
errors in the build. D81 merged: `synthetic_v2.jsonl`, 29 records and
a malformed line, written blind, its labels disagreeing on purpose with A6 and A7, a run and
debug fact; `holdout_12.jsonl` is training data from the same day. Phase 1 passed from a fresh clone and Phase 2 on the
scorer proofs; D85 and D84 merged, a run and debug fact. D86 streamed its output but missed its memory
check, so an addendum extends it to the input; D88 merged its tool, both run and debug facts. D82 merged in two halves and Phase 3 passed;
the hold-out reads 12 of 12, fitted, and the frozen set 13 of 30, both run and debug facts. D86 merged streaming the input as well,
its memory check unmet at default settings and met with the heap capped, and Phase 6 passed. D83 merged, its log line fixed to carry no
record text, and the D82 rules-file loader merged, both run and debug facts. The `--rules` flag merged, and D87 swept
every comment in `src` and `tests`, now held by the instruction check, run and debug facts.
