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

Phase 9 of `~/.agent-rules/PROJECT_PLAYBOOK.md`: Delivery and retrospective, resumed 2026-09-16. Sprints 16 and 17 were its steps 102 and 103, which sent the project back to Phase 4 (D106); that phase's check passed again on 2026-09-15, so later work resumes.
Check: the retrospective names a playbook step or a new step for every failure.
Next step: read the step 99 run in `runs\step99\` record by record, 1 of 50 passed, and name a playbook step for each failure (D122's run fact).
Open decisions: none.

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

**D1 to D31, Sprints 1 to 6 (2026-09-07 to 2026-09-08).** The contracts: the input contract
(D1); the action catalog and planner (D2, D17 to D20); output and diagnostics (D3, D22 to D24);
scheduling and the transition-day slot (D4, D21); composition, language and the call-to-action
payload (D5, D25, D26, D29); the evaluation contract, label corrections and the judge (D6, D13,
D15, D30); the twelve-record file (D9, D11); gates, replay, reference time and log scope (D8,
D10, D14, D16); the SDK and call bounds (D27, D28); the step 60 run (D31). Structure is D7, the
Phase 0 restart D12.

**D32 to D37, latency (proposed 2026-09-08, settled in Sprint 11).** From the step 60 run's
measurements: measure one call before tuning (D32), stop retrying a timeout (D33), retry only a
safety rejection (D34), bound one attempt (D35), define `p95_latency_ms` (D36, closed by D70), and
batch concurrency (D37).

**D38 to D56, Sprint 7, safety and states (2026-09-09).** One named result per safety check (D38),
all four hard gates, brand style a diagnostic (D39), which checks a record cannot switch off (D40),
what the term proxy normalizes and exempts (D41, D45, D49, D50), what earns a state (D42), the
review queue (D43), vendor retention (D44), what an exception may say (D46, D51), a null inside a
list (D47), a refusal carried through the composer seam (D48). D52 to D56 are PR #24's review fixes.

**D57 to D59, Sprint 8, structure and narration (2026-09-09).** The consent gate merged into the
channel selector, whose absence of a value answers both questions (D57); every interface without
a second implementation deleted, leaving three seams (D58); and the design diagram renumbered to
the executed order rather than the code reordered to the diagram (D59). Three run and debug facts:
comment citations corrected, four review findings on its prose, and one baseline test D58 forced
a token change in.

**D60 to D67, Sprint 9, the Phase 6 remainder and fault injection (2026-09-09).** Phase 6's
check is the playbook's (D60); latency and cost defined (D61, D62); step 81 is the Phase 6 check run
(D63); the fault-injection audit and guarded output/input paths (D64, D65); model spend kept on
failed records (D66). D67 left open. A run and debug fact records five PR #26 findings fixed.

**D68, Sprint 10, the decision log trim (2026-09-10).** How this file gets under playbook step
92's two-thousand-word cap without breaking the citations that point into it from `src/`,
`tests/` and `docs/`: every full paragraph moves to `DECISIONS_ARCHIVE.md`, which keeps the bold
heading a citation resolves by, one paragraph per sprint stays here, and the word cap plus a
check that no cited number dangles move out of prose into `check-instruction-files.ps1`, which
CI runs. Executed 2026-09-10. A run and debug fact records seven review findings fixed: what the check
counts as a definition, the S numbers and archive it missed, and four lines of stale prose.

**D69 to D73, Sprint 11, the Phase 7 evidence (2026-09-10).** The scorecard is committed under
`docs/scorecards/` (D69); `--model-call-budget-ms` lets the model answer on evaluation runs
(D70); a line that did not parse or a record that threw is an `ERROR` row, so the synthetic set
reads 12 of 13 (D71). Three live runs make `docs/VARIANCE.md` and pass Phase 7; code then took
the opt-out sentence and an unstated call to action (D72, D73), refusals fell from 33 to 0. The
sprint also took D67, D34, D33, D35, D37, did steps 86 to 88, fixed three PR #28 review findings.

**D74 to D76, Sprint 12, Phase 8 documentation (2026-09-10).** The README meets step 90 and
DESIGN.md step 91; step 95 found no secret, and the repository stays public, the owner's choice
(D74). D75 takes none of the step 96 changes. Four agents ran these steps at once, a Phase First
departure on record. Step 93 reviewed the sprint's unreviewed diff and found nothing (D76).

**D77 and D78, Sprint 13, the release (2026-09-10).** The tag `v1.0.0` is on `ccf3c5c` with its
narrative in `docs/RELEASE_v1.0.0.md`, and `docs/NARRATION.md` opens with a one-minute script; the
Phase 8 check passed at the tag, a run and debug fact. D77 asks whether the retrospective of
2026-09-06 meets Phase 9 and D78 where the narrative is published; Sprint 14 took both.

**D79, Sprint 14, Phase 9 (2026-09-10).** D77 and D78 were taken as (a): Phase 9 rests on the
retrospective of 2026-09-06, and the release is published on GitHub from
`docs/RELEASE_v1.0.0.md`. The owner read the nine step 94 files and delivered the narration,
closing Phase 8, a run and debug fact. The retrospective's new section 10 names the playbook step
for each of its sixteen findings: fourteen existing steps, one new step for finding 7, and finding
8 declined by D55. A step 104 audit of the failures since then found seven with no step and four
steps that earned nothing here; D79 took none of them, and the rules stay as they are.

**D80, cleanup (2026-09-10).** `TalkingPoints.md`, the Sprint 8 spoken script, deleted as a
duplicate of `docs/NARRATION.md` (D80). The same day, a run and debug fact: the narration was
checked against a code trace and a re-run of all three sets at the tag; eight mismatches fixed.

**D81 to D87, the architecture plan (proposed 2026-09-10).** A score of the system, a run and debug
fact, read 7 of 10 and traced the eight hold-out misses to their fields. The seven decisions it
proposed are the paragraph below, which took them all.

**D81 to D87 and D89 taken, Sprint 15 (2026-09-10 to 2026-09-11).** The owner took every
recommendation, and the project returned to Phase 0, its decisions merged in phase order from
parallel worktrees, an exception held at the merge. `synthetic_v2.jsonl`, 29 records and a
malformed line written blind, carries the honest number, 13 of 30; the hold-out is training data
at 12 of 12 (D81). Rules load from `--rules` (D82), a generic-row answer is queued (D83), one
validation per record (D84), the scorecard rebuilds its tallies (D85), and input and output
stream under a capped heap (D86). No comment cites a number (D87). Warnings are errors (D89),
and PR #33's review findings were fixed, all run and debug facts.

**D91 and D92, Sprint 15 closed, Sprint 16 opened (2026-09-11).** Sprint 15 closes with the tag
`v1.1.0` on `6e05adb`, where the narration's values were re-checked (D91). Sprint 16 closes one gap:
an empty-body completion counted with its tokens (D92).

**D94 to D105, Sprint 16 continued (2026-09-13).** The hold-out run of 2026-09-11 scored every
decision 12 of 12 and failed every message body on the judge. The payload check compares the
label's link and options (D94, D100) and the action check every stated member (D99); the judge's
reason and tokens are kept (D95); the prompt carries the stage's facts, the link, the reply options
and each call to action's purpose (D96, D103); resident links are built from the record (D97); a
no-choice call keeps its retries (D98); property facts come from `--property-data` (D101), chosen per
call to action (D104); no move date is its own branch (D102); D105 fixes three prompt misses.

**PR #36 review fixes, Sprint 16 closed (2026-09-14).** A `/code-review` and an Antigravity review
left eighteen findings: three declined against D101, A19 and no stated rule, fourteen fixed test
first, among them a judge fault that crashed the batch and a linear property scan; 919 tests at 100
percent, run and debug facts. PR #36 merged into `dev` as `b3f9036`, closing the sprint (D106).

**D106 to D112, Sprint 17 (2026-09-14).** The blind set's 24 failed checks are read record by record,
not fitted: 19 product gaps, 5 label choices (D107). D108 is taken: a tour sms offers the next two open
slots from two days after its send date, Monday to Saturday at 10:00 by default, written as dates.
D109 is taken: a record that is not a prospect gets no prospect cadence or tour option, and an unknown
call to action no link. The move timeline goes on email only (D110), tour availability only to a
named invitation (D111), and code writes renewal terms (D112). Run and debug facts hold the evidence.

**D113 to D115, the full runs after D112 and D114, Sprint 17 (2026-09-14).** Run and debug facts.
The judge was given send times (D113) and code the sms options sentence (D114); hold-out bodies went
from 7 to 8 of 11, and the blind set's two new body passes were the judge miscounting weekdays. D115
proposed code-resolved days and D116 declined it.

**D116 and D117, the blind set becomes training data, Sprint 17 (2026-09-14).** The owner took D116:
`synthetic_v2.jsonl` is fitted, and the honest number moves to the owner's larger dataset. D117 fits
twelve rules from its labels, each collision with the hold-out separated by an input. Template
result: `synthetic_v2.jsonl` 26 of 30 from 1, its failures the three declined labels, with
`sample.jsonl` and `holdout_12.jsonl` unchanged. The model runs matched it; D118 fixed two
prompt-path bugs they exposed.

**D119, PR #36's own review revisited (2026-09-15).** A second `/code-review` raised ten findings.
Six declined: two already fixed by D111/D112, two already answered by pre-merge threads this pass
missed, one an unearned catalog abstraction, one a saving too small to justify widening the composer
seam. Four fixed test first: a judge-side cancellation that discarded an already-composed record, a
case-sensitive identifier match, five near-identical `--replay` guards collapsed to one helper, and
a duplicate `ScoredRun` build removed. 116 and 916 tests at 100 percent, run and debug facts.

**D120 to D122, Sprint 17 closed, step 99 run (2026-09-16).** PR #37 merged as `7cee351`; Phase 4's
check passed again. D120 sets the honest-number run: one JSONL file, the run's own time, model, judge
and every output on, once, unread before. The first attempt failed on a missing folder, unspent;
D121 creates folders. The run scored 1 of 50 and flooded the console; D122 sends it to the named
files.
