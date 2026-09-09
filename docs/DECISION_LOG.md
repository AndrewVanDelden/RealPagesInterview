# Decision log

One paragraph per decision, in the recording form of `~/.agent-rules/ARCHITECTURE.md`: the
question, the options, the recommendation, what it scopes, the evidence, the numbered assumption
it depends on (assumptions are the table in [DESIGN.md](DESIGN.md) section 7). A task that
cannot cite a paragraph here is not scheduled. Dates are when the decision was taken; D1 to D8
were first written in the retrospective of 2026-09-06 and are restated here under the frame D9
sets.

## Current phase

Phase 6 of `~/.agent-rules/PROJECT_PLAYBOOK.md`: Structure and narration.
Check: the narration is delivered without notes and the orchestrator reads as its steps in
order. Status: not passed, not started. Phase 5 passed 2026-09-09 in Sprint 7: four safety
checks and three brand rules, each with a passing, a failing and a false-positive test, the
allow-list tested both ways, and zero violations and zero false-positive suppressions on the
synthetic set (D38 to D48).
Next step: 72 to 79, Sprint 8, Structure and narration (the Sprint 8 row of [DESIGN.md](DESIGN.md)
section 9, D7).
Open decisions: two, the D31 open question on how a real model-versus-template comparison could
be run at all, and whether the latency decisions D32 to D37 get a sprint of their own; both need
the requester.

Replace these four lines at the end of every sprint. Never append to them. A phase that passed,
the proof that passed it, the tallies it moved and any exception taken belong in the paragraphs
below and in DESIGN.md section 9, which is where the phase record lives.

## Starting decisions (2026-09-07)

**S1. What runs.** Question: the shape of the deployable. Options: the default, a library plus
a thin command-line entry point; an API or a worker. Recommendation: the default. Scopes: the
entry point parses arguments, wires the composition root, runs the per-record loop, and holds
no rules. Evidence: the statement's verbs, "reads the input record" and "produces output"; a
file arrives, a file leaves. Assumption: none.

**S2. What code decides and what is delegated.** Question: the boundary between deterministic
code and the model. Options: the default, code owns every reproducible decision and the model
writes prose and picks from a code-owned catalog under constrained decoding; the alternative,
the model decides actions. Recommendation: the default. Scopes: the policy catalog, the
composer prompt, the evaluator's semantic checks. Evidence: the statement asks for an
autonomous agent whose behavior is auditable against thresholds; a model choosing actions
cannot be replayed or proven. Assumption: A18.

**S3. Where the seams are.** Question: where interfaces exist. Options: the default, one seam
per external or non-deterministic dependency with a real and an offline implementation on the
day it is created; the alternative, an interface per component. Recommendation: the default.
Scopes: three interfaces stay (composer, completion client, safety validator); every other
interface goes as the sprint that touches it lands (D7); time is a value passed in (D10).
Evidence: at the retrospective eleven interfaces existed and eight had one implementation and
no test substitute. Assumption: none.

**S4. How done is measured.** Question: whether evaluation is part of the deliverable.
Options: the default, an evaluator built before the product and proven able to fail; the
alternative, none. Recommendation: the default. Scopes: Sprint 2 precedes every decision-code
sprint; the definition of complete in D6. Evidence: the retrospective's finding 5, a scorer
that could not fail on two of its own fields. Assumption: none.

## Decisions

**D1. Input contract (revised 2026-09-07).** Question: what the reader accepts, reports, and
refuses. Options: a hand list of required fields fitted to the files seen; or three required
members and everything else optional with a named default. Recommendation: the second.
`task_id`, `consent`, and `channel_preferences` are required and a line missing one is an
error row naming the member; every other member is optional, its absence gets a default and a
diagnostics line naming the field; members the record types do not declare are kept as
extension data, listed in diagnostics, and logged per record; a value-type property on an input
record is either required or nullable, enforced by a reflection test over every record type.
Scopes: D2, D4, D5, Sprint 2. Evidence: DESIGN.md section 2; the statement promises cases the
samples do not show; the retrospective's finding 4 (silent year-0001 dates). Assumptions: A16,
A17. Landed 2026-09-08 in Sprint 2, with one difference from the paragraph above: the
reflection test over the record types is not written, because every value-type member is now
nullable by construction and the reader test over `holdout_12.jsonl` (12 of 12 rows) is the
check that holds; the test returns to scope if a non-nullable value-type member is ever added.

**D2. Decision model (revised 2026-09-07).** Question: how `next_action` is chosen. Options: a
policy table with one row per stage observed in the twelve; or a catalog with the rows the
two samples justify plus one generic row, and a semantic score for what the catalog cannot
name. Recommendation: the second, under D9. Consent first: not contactable gives `no_op` with
reason `no_contact_consent` and nothing else runs. The catalog is one data file keyed on
persona and stage, carrying the action template and the default call to action; the rows are
prospect `new` and prospect `open` from the samples and the generic row; the horizon rule of A7
picks the template's branch; a persona or stage with no row uses the generic row and the
diagnostics name the fallback; the planner returns a `Result` and never applies the nearest
rule to a record it cannot classify. `NextAction` is one record with `Type` and nullable
`Name`, `Value`, `Reason`, nulls omitted on the wire. Scopes: Sprint 4, the orchestrator order.
Evidence: section 3 of DESIGN.md; playbook steps 41 to 43. Assumptions: A7, A8. Landed early,
2026-09-08 in Sprint 2: consent first with `no_op` and its reason, and the `NextAction` shape.
The catalog, the generic row, and the `Result` from the planner remain Sprint 4.

**D3. Output contract.** Question: what suppression looks like and what diagnostics carry.
Options: a null `next_message`; or an object with channel `none` and null fields.
Recommendation: the object, since the output always has both members; the evaluator treats
both spellings and a null channel as one value. Diagnostics carry `suppression_reason`
(`none`, `no_contact_consent`, `composition_failed`, `safety_violation`), the decision inputs,
every defaulted field, every fallback, and the earned states: `consent_verified` only when the
selector ran, `fair_housing_check_passed` as the validator's verdict, `brand_style_applied` as a
check that can fail, any other name as not earned. Scopes: Sprint 2, Sprint 7. Evidence: the
statement's "(or not sent)"; A2, A14. Landed 2026-09-08 in Sprint 2: the object with channel
`none`, `suppression_reason` in diagnostics, and the ingest notes (defaulted fields, unknown
members) on every diagnostics row. The earned states remain Sprint 7.

**D4. Scheduling (revised 2026-09-07).** Question: what `send_at` is a function of. Options:
per-stage day offsets and minutes fitted to the twelve; or the channel slot on the first day at
or after max(reference time, `last_interaction`) in the record's timezone, minutes not modeled.
Recommendation: the second, under D9. Scored to the day and to the hour. Unknown timezone is
UTC plus a diagnostic; daylight-saving transitions get property tests. Scopes: Sprint 5, D6's
`send_at` rule. Evidence: section 3; the two samples fix the hour by channel and nothing else.
Assumptions: A4, A5, A6.

**D5. Composition (revised 2026-09-07).** Question: what the message is a function of.
Options: templates per stage fitted to the twelve, Spanish included; or templates keyed on
persona and channel built from the facts the record carries, in English, with any other
language delegated to the model composer and reported in template mode. Recommendation: the
second, under D9. The call-to-action type comes from `primary_cta` through the vocabulary
table, unknown values pass through, absent means the generic `reply`; sms carries numbered
options, email carries a link built from the property slug; the model prompt carries persona,
stage, language, every date the record has, the stated interests, and the catalog's call to
action, with structured output constraining the type. Body semantics are scored by the judge,
not asserted. Scopes: Sprint 6. Evidence: section 3; A9 to A13.

**D6. Evaluation contract (revised 2026-09-07).** Question: what is scored and against which
data. Options: score the twelve as a training set; or score both the twelve and a synthetic
set as evaluation sets, never fitted to. Recommendation: the second, under D9. Fields and
granularity are DESIGN.md section 6. The scorer proof runs in the suite. Definition of
complete: every record in both sets produces a valid output row with diagnostics that explain
it; the deterministic checks the input can decide (channel, consent, language, opt-out, safety,
payload shape) pass on every record; the honest numbers for `send_at` and `next_action.type`
are reported in the README and never targeted. Scopes: Sprint 2, the README. Evidence: S4; the
requester's answer, D9. Assumption: A19. Landed 2026-09-08 in Sprint 3: every field of
DESIGN.md section 6 with a three-way verdict, the scorer proof on three sets in the suite,
`synthetic_12.jsonl`, `--replay`, and the baseline numbers pinned by `BaselineNumbersTests`.
One difference from the paragraph above: the semantic judge is deferred to Sprint 6 (D15).

**D7. Structure.** Question: what stays, what goes, what the orchestrator reads like. Options:
collapse interfaces up front; or let each sprint delete the interface it touches.
Recommendation: the second. Kept: composer, completion client, safety validator. Removed as
touched: consent gate, channel selector (merged into one `Select` returning an option),
scheduler, planner, agent, evaluator, record writer; the record reader is already concrete.
The orchestrator has six numbered steps, one comment each, matching the DESIGN.md diagram.
Scopes: Sprint 8. Evidence: S3. Assumption: none.

**D8. Gates.** Question: what runs without a person remembering. Options: the default,
`.github/workflows/test.yml` running `dotnet build` and `.\test.ps1` on every push and pull
request with branch protection on `dev` requiring the `test` check; or none. Recommendation:
the default, taken 2026-09-07. `test.ps1` exits with the `dotnet test` exit code so the check
can go red. Applied 2026-09-07 after the first green run on PR #16: `dev` requires the `test`
context, refuses force pushes and deletion, and does not enforce on admins, so the owner can
override in an emergency. Scopes: Sprint 1; every later PR. Evidence: the retrospective's finding that
nothing governed truth against data between sprints. Assumption: none.

**D9. What the twelve-record file is (2026-09-07).** Question: whether rules may be fitted to
`holdout_12.jsonl`. Options: treat it as the training set and fit a per-stage table; or treat
it as unknown, build from the two-record file, and use its curveball categories only to shape
the risk register. Recommendation: the second, on the requester's answer that the twelve are
unknown to the design and future sets may be harder. Scopes: D2, D4, D5, D6, the definition of
complete. Evidence: the requester, 2026-09-07; the statement, "learns what to do only from
input data." Assumption: A19.

**D10. Reference time (2026-09-07).** Question: how a run learns the date its send times are
relative to. Options: a `--now` flag with the current UTC time as default; or a default derived
from the latest `last_interaction` in the batch. Recommendation: the flag, on the requester's
answer. The `--now` flag does not exist yet; it lands with the CLI contract work in Sprint 3.
The documented run against the twelve passes `--now 2025-12-09T00:00:00-06:00`, and the
README records that command. Scopes: D4, the CLI, Sprint 2. Evidence: section 3, the send-day
row; the field is absent from most records. Assumption: A4. Landed 2026-09-08 in Sprint 2:
`--now` on the CLI, logged once per run, passed as a value to the agent, the planner, and
the scheduler.

**D11. Where the twelve-record file lives (2026-09-07).** Question: whether CI can read the
evaluation set. Options: in the repo beside `sample.jsonl`, linked into the test output; or
outside the repo, scored by hand. Recommendation: in the repo, on the requester's answer.
Scopes: Sprint 2's fixtures, D8's check. Evidence: a file only two session scratchpads held
would have been lost. Assumption: none.

**D12. Phase 0 restart (2026-09-07).** Question: whether Phase 0 restarts at step 1 or
continues at step 4. Options: either. Recommendation: restart, on the requester's answer;
DESIGN.md is rewritten from step 1 and the retrospective's section 7 plan is superseded by
DESIGN.md section 9. Scopes: everything after. Evidence: the retrospective's section 9.
Assumption: none.

**D13. Scorer proof against the labels (2026-09-08).** Question: what to do when a scorer
proxy fails the oracle's own label (playbook step 33: the labels passed as actuals must
score 100 percent). Options: keep the proxy and report the oracle as failing; or treat a
proxy that fails a label as a wrong proxy, correct the scorer, and record the record that
forced each correction. Recommendation: the second. The scorer is the measuring instrument,
not a product rule: D9 keeps every product rule fitted to the two samples, and validating
the metric against every label is what an evaluation set is for. Every correction is listed
here with its record, so the proxy's evidence is auditable. Corrections: (a) personalization
facts are the first name and the property name, searched over subject plus body; city and
amenities are not counted (sample 1's body omits the city at threshold 0.85; hold-out 11's
body omits both amenities at 0.8; hold-out 6 names the property only in the subject); a
multi-word fact is covered when at least half its words appear as whole words (every label
says "Oak Ridge" for "Oak Ridge Apartments"). (b) the opt-out instruction is the whole word
STOP in capitals, or opt out, opt-out, unsubscribe, after the unicode hyphens U+2010, U+2011,
U+2013 and U+2014 are folded to a hyphen (hold-out 6 carries only "Opt‑out" spelled with
U+2011; hold-out 11 carries "Responde STOP"); one list, shared by the validator and the
scorer, so the agent can never emit what the scorer rejects. (c) the body language is
detected by a stop-word count over subject plus body for the languages the sets contain,
English and Spanish; a stated language the detector does not know is reported as not
measured. (d) the call-to-action type is scored against the label's own `cta.type`, never
against the product's vocabulary table (hold-out 3 labels `reschedule` for the constraint
`reschedule_tour`, hold-out 7 labels `intent_capture` for `reply_intent`; a scorer that
consulted the table passed the product's own guess back to itself, the retrospective's
finding on the CTA field). Scopes: Sprint 3; A12, A15; the validator's opt-out check. Evidence: the proof
runs recorded in DESIGN.md section 9 under "Numbers after Sprint 3". Assumption: A15.

**D14. Replay alignment (2026-09-08).** Question: how `--replay` matches the rows of an
output file to the records of `--input` when the output carries no task id (playbook step
27: nothing extra in the graded output). Options: add `task_id` to the output; or align by
position over the records that parsed, and refuse with a usage error naming both counts when
they differ. Recommendation: the second. A safety violation count and a latency exist only
in the run that produced the file, so replay reports both as not measured. Scopes: the
`--replay` flag, OPERATIONS.md. Evidence: D3; the output writer appends one row per record
that ran, in input order. Assumption: none.

**D15. Judge deferred to Sprint 6 (2026-09-08).** Question: whether the semantic judge for
`next_action.type` (DESIGN.md section 6) lands in Sprint 3 or with the body judge in Sprint
6. Options: build the judge seam now, off by default; or defer it to Sprint 6, where D5
already places judge-scored body semantics, so one pinned model and one rubric have one
owner. Recommendation: the second; Sprint 3 scores every deterministic field of section 6
and reports the exact-match number for the action type. Scopes: Sprint 3, Sprint 6.
Evidence: playbook step 31, the judge is one signal beside the deterministic checks; D5.
Assumption: A8.

**D16. One log scope owner (2026-09-08).** Question: which of `CliRunner`,
`LeasingMessageAgent`, and `Evaluator` opens the `TaskId` log scope. Options: the CLI's
batch loop; or the agent, so any caller gets the scope for free. Recommendation: the CLI. It
is the one place that knows both the task id and the batch position, the evaluator runs
inside its loop, and the agent's own scope produced the duplicated `TaskId=x TaskId=x` on
every line (the retrospective's logging defect 1). A library caller that wants correlation
opens its own scope the way the CLI does. Scopes: Sprint 3, OPERATIONS.md section 3.
Evidence: the retrospective's logging defect 1 and Fix 8. Assumption: none.

**D17. Catalog storage (2026-09-07).** Question: whether the action catalog D2 calls "one data
file" is a data file at run time or a table compiled into one source file. Options: an
external JSON file behind a flag; an embedded JSON resource parsed at startup; or a table in
one source file, `src/Agent/Decisions/ActionCatalog.cs`. Recommendation: the third. Playbook
step 38 says a decision unit takes its inputs and returns a decision with no I/O, and the
catalog holds three rows that nothing has asked to change without a rebuild, so a file format
is not earned (LC, step 41). A8's "configurable: the catalog file" is met by one file to edit,
and the compiler checks the rows. The failures a parser would have caught are checked instead
by `ActionCatalog.Create`, which every construction goes through, including `Default`. Scopes:
Sprint 4, A8's configurable column. Evidence: playbook steps 38, 41, 42; three rows in
DESIGN.md section 3. Assumption: A8. Revises D2's wording, which said "data file".

**D18. Where the planner's failure lives, and what the planner returns (2026-09-07).**
Question: D2 says the planner "returns a `Result` and never applies the nearest rule to a
record it cannot classify", but A8 gives every unmatched record the generic row, so no record
is unclassifiable and `Plan` has no reachable failure. Options: `Plan` returns
`Result<PlannedAction>` and fails when the catalog cannot classify; or the `Result` moves to
`ActionCatalog.Create` and `Plan` returns the decision object directly. Recommendation: the
second. The first is either a branch no honest test can reach under the 100 percent gate, or
it forces the planner to take an unvalidated catalog and re-check it once per record. D2's
guarantee becomes structural instead: the lookup is exact key, then the generic row, and there
is no nearest-match path in the code to disable. `ActionCatalog.Create` returns
`Result<ActionCatalog>` and fails on a row with a blank persona or a blank lifecycle stage, on
two rows with the same key (compared case-insensitively, the way the lookup compares), and on
an action type outside `ActionTypes.All`. All three are reachable from a test that builds a
catalog. `Plan` returns `PlannedAction`, which carries the `NextAction` and the why that
playbook step 39 asks for: the horizon branch, the horizon in days (null when the record
states no move date), and which row supplied the action. Scopes: Sprint 4, `AgentDiagnostics`,
the Phase 3 check that diagnostics explain every decision. Evidence: playbook steps 39, 43;
A7, A8; the coverage gate in AGENTS.md. Assumptions: A7, A8. Revises D2's `Result` clause.

**D19. Default call to action deferred to Sprint 6 (2026-09-07).** Question: whether the
catalog row carries D2's default call to action in Sprint 4 or in Sprint 6. Options: add the
column and wire the composers to it now; add the column unread; or defer the column. Wiring it
now pulls Sprint 6's catalog-driven call to action into Sprint 4 and edits both composers.
Adding it unread leaves a property no code reads, and the coverage gate would then need a test
written only to touch it, which AGENTS.md forbids. Recommendation: defer. Sprint 4 ships
persona and stage to an action template plus the fallback; Sprint 6 adds the column and moves
`PrimaryCtaVocabulary.GenericCtaType` onto it, at the point where a composer reads it. Scopes:
Sprint 4, Sprint 6, A9. Evidence: DESIGN.md section 9, the Sprint 6 row; LC. Assumption: A9.

**D20. `NextActionPlannerOptions` deleted (2026-09-07).** Question: what happens to the
planner's settings record once catalog rows carry the cadence name and the follow-up days.
Options: keep it holding only the short-horizon threshold; or delete it and name the threshold
as a constant. Recommendation: delete. A7 marks the threshold not configurable, and playbook
step 41 earns a setting with a second known value, of which there is none; keeping it leaves a
settings record with one member that the design says is not a setting, plus two guard-clause
tests for a value nothing configures. The 45 days become
`NextActionPlanner.ShortHorizonThresholdDays`, cited to A7 where it is defined. Removes
`src/Agent/Decisions/NextActionPlannerOptions.cs` and
`tests/Agent.Tests/Decisions/NextActionPlannerOptionsTests.cs`. Scopes: Sprint 4. Evidence:
A7's configurable column; playbook step 41. Assumption: A7.

**D21. The slot on a transition day (2026-09-08).** Question: what instant `send_at` names when
the channel's local slot (A5) does not exist on the send day, because the zone springs forward
across it, or occurs twice, because the zone falls back across it. Options: keep the current
arithmetic, which stamps `TimeZoneInfo.GetUtcOffset(wall time)` on the wall time and so emits,
for a slot inside a gap, an offset the zone never had at that instant, and picks the second of
two occurrences for an ambiguous slot without saying so; or resolve a nonexistent slot to the
first instant that exists at or after it, which is the transition instant, and an ambiguous
slot to the earlier of its two instants; or skip the day and take the next day's slot.
Recommendation: the second. `send_at` is an instant, and an instant the zone never had is
wrong in a way no downstream reader can detect, since the offset travels with the value. The
earliest valid instant at or after the stated slot keeps A4's floor property, that the send is
never earlier than max(reference time, `last_interaction`), and stays closest to A5's hour;
skipping the day moves the send day, which A4 fixes independently of the hour. The resolution
is a value in the diagnostics (D22), not a silent correction. Scopes: Sprint 5,
`Agent.Common.TimeZones`, `SendScheduler`, `AgentDiagnostics`. Evidence: none in the data.
Both samples are `America/Chicago` at 09:00 and 10:00, and no transition in the current zone
database covers those hours, so no record observed or synthetic reaches either branch. The
rule is stated over a zone's adjustment rules, not over today's zone database, and is proved
against custom zones built in the test whose transitions do cover the slot, plus a sweep over
every system zone's real transitions. Assumption: A20.

**D21 addendum, PR #20 review (2026-09-08).** The first implementation did not match this
decision: `ResolveSlot`'s gap branch shifted the wall time forward by the gap's own width
(`GetUtcOffset` before the transition, re-stamped after) rather than landing on the transition
instant. The two agree only when the requested slot falls exactly at the gap's start; for any
slot farther into the gap, the shift overshoots past the transition instant by however far into
the gap the slot fell, which contradicts the "stays closest to A5's hour" reasoning above.
Flagged in PR #20 by a Claude review and independently by an Antigravity (Gemini 3.8 Flash)
review comment on the same line. Fixed by having `ResolveSlot` binary-search
`TimeZoneInfo.IsInvalidTime` for the earliest valid instant instead of computing an offset
shift, since the zone's own transition boundary is not exposed by public `TimeZoneInfo` API.
No tally on any of the three sets moved: no zone in the current database reaches this branch
(A20), so the bug was invisible to every check the product runs, only to the property tests
built to cover it, and to review.

**D22. What the diagnostics say about `send_at` (2026-09-08).** Question: whether the schedule
decision gets the account D18 gave the action plan, and what is in it. Options: leave `send_at`
unexplained, since the ingest notes already name an unrecognized timezone; log the transition
branch only when it fires, as playbook step 43 asks; or carry a `ScheduleNotes` object beside
`ActionPlanNotes`, naming which input was the floor, the zone the send was computed in, and how
the slot resolved. Recommendation: the third. The Phase 3 check is that the diagnostics explain
every decision, `send_at` is a decision with three inputs (A4's floor, A6's zone, A5's hour),
and a log line only reaches a reader who kept the log. The object is null on a record the
scheduler never ran for, the same rule `ActionPlanNotes` follows for a record the planner
never ran for: consent suppression, or a composer that produced no message to schedule. A
record the final safety check suppressed keeps both.
Scopes: Sprint 5, `AgentDiagnostics`, `LeasingMessageAgent`, `SendScheduler`'s return type.
Evidence: the Phase 3 check in AGENTS.md; D18's precedent; playbook step 43. Assumptions: A4,
A5, A6, A20.

**D23. The channel decision gets no diagnostics object (2026-09-08).** Question: the Phase 3
check is that the diagnostics explain every decision, and after D18 and D22 the consent gate,
the planner and the scheduler each have an account while the channel selector has none; whether
that closes the check. Options: add a `ChannelNotes` object naming which entry of
`channel_preferences` won and which entries consent ruled out, and hold Phase 3 open until it
lands; or state the rule that earns an account and close the check under it. Recommendation:
the second, and Phase 3 is passed. The rule: a decision earns a diagnostics object when its
working cannot be read off the input and the output. The horizon branch, the horizon in days
and the row that answered are internal to the planner (D18); the floor, the zone and the slot
are internal to the scheduler (D22); the channel's working is not internal at all. It is
`channel_preferences` in the record's stated order intersected with `consent`, both of which
the input carries, and `next_message.channel` is the answer, so a reader with the record and
the row can reproduce the selection exactly, and `consent_verified` and `suppression_reason`
already say when the intersection was empty. An object restating those two fields would be the
only diagnostics member that tells a reader nothing the two files in front of them do not.
Scopes: the Phase 3 check in AGENTS.md, Sprint 5, any later sprint tempted to add the object.
Evidence: A1 and A3, which state the rule entirely in input fields; DESIGN.md section 3's
channel row; LC. Assumptions: A1, A3. Confirmed by the requester on 2026-09-08.

**D24. The composer's identity in the diagnostics (2026-09-08).** Question: playbook step 57
says the diagnostics record which implementation produced each result, and the Phase 4 check
says the diagnostics say so on every record; nothing in the output or the diagnostics names the
composer today. Options: log the composer once per record and leave the diagnostics alone; add a
string member to `AgentDiagnostics`; or have the composer return its working beside the message,
the way `SendScheduler` returns `ScheduledSend` (D22), and carry it as a `CompositionNotes`
object. Recommendation: the third. A log line only reaches a reader who kept the log, and the
composer's identity is exactly the kind of working D23 says earns an object: it cannot be read
off the input and the output. `IMessageComposer` returns `Result<ComposedMessage>`, where
`ComposedMessage` is the `NextMessage` plus `CompositionNotes(Composer, Attempts,
NetworkRetries)`: which implementation produced the text, how many compose calls the
compose-validate loop made, and how many transport retries the call underneath spent (null for a
composer that makes no network call). `ValidatingMessageComposer` returns the notes of the
attempt that answered, with its own total attempt count. Silent degradation is visible without a
boolean: the run states the composer it asked for (`--composer`), and a record whose notes say
`template` on an `openai` run is one the fallback answered. Scopes: Sprint 6, `IMessageComposer`,
both composers, `ValidatingMessageComposer`, `AgentDiagnostics`, the Phase 4 check. Evidence:
playbook steps 49 and 57; D22's precedent; D23's rule for what earns an object. Assumption: A18.

**D25. The call-to-action payload and the link host (2026-09-08).** Question: A10 says sms
carries numbered reply options and email carries a link, and the payload check has been 0 of 10
on every set since Sprint 3 because the template composer emits neither. Where do the options and
the link come from, given no input field states either. Options: hard-code one pair of options
and one link in the template composer; or put both on the catalog row D19 deferred, keyed on the
call-to-action type, so the vocabulary lives in the one table playbook step 42 asks for.
Recommendation: the second. `ActionCatalog` gains the call-to-action column D2 named and D19
deferred: per call-to-action type, the sms reply options and the email link path. The link is
`https://{slug}.example/{path}` (A10), and the slug rule is A21. A record with no property name
has no host, so it gets no link and the payload check fails honestly rather than being fed an
invented host. `PrimaryCtaVocabulary.GenericCtaType` moves onto that column, which is what D19
said Sprint 6 would do. Scopes: Sprint 6, `ActionCatalog`, both composers, A9, A10, A21.
Evidence: sample 1's `options` ["Thu","Fri"] and sample 2's `link`
`https://oakridge.example/tour`; playbook step 42; D19. Assumptions: A9, A10, A21.

**D26. Language is passed through, never gated (2026-09-08).** Question: A13 says the template
set ships English and any other language goes to the model composer, which leaves every
non-English record failing the language check on the offline path the Phase 4 check runs.
Options: keep A13 as written; add a language allowlist and refuse anything outside it; or state
that no component gates on a language allowlist, ship the offline template sets for the languages
there is evidence for, and name the fallback when a record's language has no set. Recommendation:
the third, on the requester's answer of 2026-09-08: nothing in the problem statement makes
English a rule, `input.language` is a free tag, and a model composer is not English-only either.
The model path passes the record's language to the model with no allowlist and no list of
supported tags anywhere in the code. The template composer holds one template set per language it
can serve, English and Spanish today, keyed on the parsed tag; a tag with no set is served in
English with the `locale_not_applied` diagnostic A13 names, which is a stated limit of a template
file, not a rule about which languages a prospect may use. Spanish is earned by the synthetic
set, which section 4 item 4 put there before any decision code existed, so D9 is not touched.
Scopes: Sprint 6, `TemplateMessageComposer`, `OpenAiMessageComposer`, A13. Evidence: the
requester, 2026-09-08; playbook step 59; DESIGN.md section 4 item 4. Assumption: A13, revised.

**D27. The real client goes through the official SDK (2026-09-08).** Question: playbook step 49
and the SCS pillar both say the real client is written against the official SDK when one exists,
and `OpenAiCompletionClient` is a hand-rolled `HttpClient` call against
`/v1/chat/completions`. Options, and when each wins: (a) raw HTTP, which wins only when no
official SDK exists or the deployment forbids the dependency, and costs you the request and
response shapes, the retry policy, and the structured-output plumbing by hand; (b) the official
`OpenAI` package, which wins when the product calls OpenAI itself and wants the vendor's own
model of the API surface, its structured-output types and its retry policy, at the price of one
vendor dependency; (c) `Microsoft.Extensions.AI` `IChatClient`, which is Microsoft's recommended
abstraction and wins when the application wants provider portability plus middleware for
telemetry, caching and function calling, and which sits on top of (b) rather than replacing it;
(d) an agent framework such as Semantic Kernel or Azure.AI.OpenAI, which wins when you want
planners, memory and connectors, or an Azure-hosted deployment and its auth. Recommendation: (b),
on the requester's answer of 2026-09-08. This product already owns the portability seam that (c)
would sell it: `ICompletionClient` is one method with a real and a fake implementation (S3), so
`IChatClient` would be a second abstraction over the first, which EA refuses. (d) brings an
orchestration layer for a program whose whole point is that code owns every decision (S2). The
`OpenAI` package is pinned at an exact version in `Directory.Packages.props`, and every type,
parameter and flag used is confirmed against that restored assembly, not against documentation
or memory (step 50). Scopes: Sprint 6, `OpenAiCompletionClient`, its tests, D28. Evidence: SCS;
playbook steps 49 and 50; S3. Assumption: none.

**D28. What a model call is bounded by (2026-09-08).** Question: playbook step 49 asks for a
per-call timeout below the latency budget, bounded retry with backoff on transient failures, and
the retry count in the diagnostics; the hand-rolled client had none of the three. Options: rely
on the SDK's defaults, which are three retries with exponential backoff and its own network
timeout; or state each bound in the composition root and count the retries. Recommendation: the
second. The strictest `p95_latency_ms` any record states is the budget the run is measured
against, so the timeout is a stated value rather than a default, and a retry count nobody can
see is the silent degradation step 57 exists to expose. The temperature is set low and
reproducibility is never claimed from it (step 52); variance is measured in Phase 7 step 83.
Scopes: Sprint 6, `OpenAiCompletionClient`, `CompositionNotes.NetworkRetries`, D24. Evidence:
playbook steps 49 and 52. Assumption: A15.

**D29. What the model is told, and what it is told to ignore (2026-09-08).** Question: step 55
says every field that changes what the message should say reaches the model, and the prompt
carries four; D5 names persona, stage, language, every date the record has, the stated interests
and the catalog's call to action. Options: add the fields to the existing block; or add them and
pin the result. Recommendation: the second. Every record-derived value goes inside
`<prospect_data>`, every instruction stays outside it, and both are pinned by golden tests over
the built prompt and the serialized request (step 58) so a wording change is a reviewed diff
rather than a silent one. The boundary is tested with records carrying an instruction in a name
field and in a free-text field, and the assertion is that the produced message does not follow
it (step 54). Scopes: Sprint 6, `OpenAiMessageComposer`, its tests. Evidence: playbook steps 53
to 55 and 58; D5. Assumptions: A9, A12, A13.

**D30. The judge (2026-09-08).** Question: D15 deferred the semantic judge for
`next_action.type` to Sprint 6, where D5 also puts body semantics; a judge is a network call and
the Phase 4 check runs offline. Options: defer it again; drop it and let exact match be the whole
contract; or build it behind a flag, reference-based, pinned. Recommendation: the third, taken
after reading the current guidance on judges. Three properties follow from what makes judges
unreliable. It is reference-based: the rubric asks whether the produced message conveys the
label's own offer, call to action and facts, never whether the message is good, because a judge
scoring quality with no reference is the setting where position, verbosity and self-preference
bias have been measured. It is one signal beside the deterministic checks and can never overturn
one, which is step 31. It is off unless `--judge` is passed, and its two checks read as not
measured on every offline run, so the Phase 4 check and every pinned baseline are untouched by
it. The judge model is pinned separately from the composer model and named in the report. The
known limitation, recorded rather than papered over: with one vendor key the judge and the
composer can be the same family, which is the self-preference setting; on the template path the
text being judged is not model-written at all, and on the model path the label is the reference,
which is the mitigation the literature gives. No new interface: the judge is a class over the
existing `ICompletionClient` seam, whose fake already exists (EA). Scopes: Sprint 6,
`Agent.Evaluation`, the CLI, DESIGN.md section 6, D5, D6, D15. Evidence: playbook step 31; the
judge-bias guidance summarized above; S4. Assumptions: A8, A15.

**D25 addendum, PR review (2026-09-08).** The catalog shipped with an sms option list on
every row, and after D26 moved the prose into the language sets nothing read it: the option
text a message carries comes from `MessageTemplates.SmsOptions`, keyed on the call-to-action
type, while the catalog's copy was keyed on `primary_cta` and could drift from it with nothing
to catch the drift. Flagged in a cold-context review of the sprint diff. The column is removed;
`CallToAction` is now the type and the link path, and the header comment no longer claims to
own an option list it does not. The rule that survives: the catalog owns what a call to action
is and where its link points, and the language sets own every word a person reads (LC, HSC).

**D28 addendum, PR review (2026-09-08).** The paragraph above says the per-call timeout comes
from the strictest stated `p95_latency_ms`, and the first implementation handed that number
straight to `NetworkTimeout`, which bounds one attempt. With `MaxRetries = 1` beside it, a
2000 ms budget was measured taking 4828 ms over two attempts, and the compose-validate loop's
second call can double that again, so the bound the code and the docs stated was not the bound
the code enforced. Flagged in a cold-context review of the sprint diff, measured against a hung
transport. Fixed by dividing: `OpenAiCompletionClient.PerAttemptTimeout` is the budget over
`1 + MaxRetries`, so one call and its retry fit inside the number the record stated. What is
still not bounded is stated rather than hidden: after a failed call the compose-validate loop
composes once more before falling back, so a record that fails composition can spend up to
twice its budget before the template composer answers, and the p95 check measures that.

**D31. Which implementation the product uses on these sets (2026-09-08).** Question: playbook
step 60 says to run the real implementation against the examples, compare its score to the
offline one, record both per case type, and choose with a stated reason. Options: the model
composer, the template composer, or a mix per case type. Recommendation: the template
composer, on the measurement rather than on preference. The run was made on 2026-09-08 with
`--composer openai` against all three sets, and the model answered no record at all: every
record's `composition` reads `template` with `attempts` 3, which is two model attempts and the
fallback. The cause is stated in D28 and was predicted before the run: the strictest stated
`p95_latency_ms` on these sets is 2000 ms, the client divides that into two 1000 ms attempts,
and a `gpt-4o-mini` completion of this size does not return in 1000 ms. So the comparison step
60 asks for is degenerate on this data: both paths score identically because the same composer
wrote every message. What the run does measure is the degradation path end to end, on real
network calls: 23 records, 46 model calls, 92 HTTP requests, every one abandoned at its
timeout, no record lost, no output row missing, and the diagnostics naming the fallback on
every record. The p95 check, which passes at 18 ms offline, fails at about 5700 ms here, which
is the honest cost of trying. Scopes: DESIGN.md section 9, the Phase 4 close, any future
comparison run. Evidence: the run recorded in DESIGN.md section 9 under "Numbers after the step
60 run". Assumptions: A15, A18.

**D31 open question, for the requester.** A real model-versus-template comparison needs the
model to answer at least once, and on these sets it cannot while the timeout is derived from
the records' own stated budget. Three ways out, none taken without a decision: state that the
stated budget and a live model are incompatible and leave the offline path as the answer, which
is what this paragraph does today; add a documented override flag so a comparison run can raise
the budget without editing the evaluation data, which is a product change earned only by this
need; or treat `p95_latency_ms` as a reporting threshold rather than a call timeout, which
contradicts playbook step 49 and D28. The first is free and honest, the second costs a flag and
another live run, the third reopens a decision.

**D31 addendum, the measured cost (2026-09-08).** The run cost about $0.004, read from the
vendor's usage page: `gpt-4o-mini` input $0.002 and output $0.002 over roughly 12,100 tokens.
The client made 92 HTTP attempts and the vendor recorded about 30 requests, so a call abandoned
at its timeout usually never becomes a billable request, and sometimes does: the third that
completed server-side were billed with their output tokens. That is worth knowing before
choosing among this decision's three ways out, because the option that raises the budget to get
a real comparison would pay for every call in full rather than for a third of them.

**D24 addendum, PR #21 review (2026-09-08).** A recall-biased review of PR #21's diff found
three defects under D24's own subject, the composer's identity and its notes. (a) The email
link for a call to action was built from `CallToActionCatalog.Resolve(primaryCta)`, the
record's stated constraint, rather than from `payload.CtaType`, the type actually placed on
the outgoing message; the two are only guaranteed equal when `primary_cta` is present, so an
absent constraint (the schema leaves `cta_type` unconstrained in that case, see D5) let the
model choose a specific type while the link still pointed at the generic path. Fixed by
`CallToActionCatalog.LinkPathForType(ctaType)`, a reverse lookup from the type actually on the
wire, called with `payload.CtaType` rather than the input constraint. (b) The same site
resolved the catalog twice on one unmodified input, once for the required type and again for
the link path; `TemplateMessageComposer` already resolved once and reused both fields.
`OpenAiMessageComposer` now does too, since the type it enforces (`requiredCtaType`, from the
constraint) and the type it links (`payload.CtaType`, from the model) are different resolves
by design after (a), not the same call repeated. (c) Both composers hardcoded `Attempts: 1` on
the `CompositionNotes` they built, a value `ValidatingMessageComposer.WithAttempts`
unconditionally overwrites on every production path; `CompositionNotes.ForComposer` states the
placeholder once instead of once per composer. Each fix carries a test that fails against the
prior code, confirmed by reverting the fix and re-running it. Tallies on all three sets: no
tally moved, since `--composer openai` still falls back to the template composer under D31's
timeout math on these sets, so the OpenAI composer's own output has never been measured
against a label.

**D3 addendum, PR #21 review (2026-09-08).** D3's rule that a suppressed record's diagnostics
carry no composer identity had a third case unhandled: `AgentDiagnostics` is built from the
compose step's notes before the final safety check runs, and the final-safety-suppression
branch returned that same object unchanged, so a record with no message on the wire could
still carry a non-null `composition`, naming a composer as if its draft had shipped. The other
two suppression cases (consent, composition failure) already null the field. Fixed by nulling
`Composition` on that branch too. A test asserting the null now sits beside the existing
assertions on `RunAsync_FinalSafetyValidationFindsViolations_SuppressesMessage`, confirmed to
fail against the prior code.

**D28 addendum, PR #21 review, part two (2026-09-08).** `CompositionNotes.NetworkRetries` read
only the winning compose attempt's own count; a retry spent on an attempt
`ValidatingMessageComposer` rejected for a safety violation and discarded was lost, so a
record whose first attempt retried once and second attempt succeeded cleanly reported zero
retries despite three HTTP requests. Fixed by summing a discarded attempt's
`NetworkRetries` (when the attempt built a `ComposedMessage` at all; a `Result.Failure`
attempt carries none, since the failing composer never built one, and that gap is stated
rather than closed here) into the winning attempt's own count in `WithAttempts`, preserving
null when neither the winner nor any discarded attempt made a network call. Two tests cover
the sum (a discarded attempt then a clean winner) and the null-to-real transition (every
attempt discarded, the fallback answers with retries carried in from before it).

**D26 addendum, PR #21 review (2026-09-08).** `MessageTemplateCatalog.Resolve`, D26's own
mechanism, restated the BCP-47 primary-subtag split `LanguageDetector.TryParseTag` (D13 c)
already implements, independently, in a different namespace. Fixed by extracting the split
into `Agent.Common.Bcp47.PrimarySubtag`, called from both; no behavior changed, since both
sites matched the same two tags the same way, only the split itself moved to one place.

**D30 addendum, PR #21 review (2026-09-08).** Two findings under D30's subject, the judge.
(a) `SemanticJudge.BodyOf` restated `Evaluator.AsPresent`'s "null message and a channel-none
message are one value" rule (D3) inline instead of calling it; fixed by making `AsPresent` and
`EffectiveChannel` internal rather than private, so `BodyOf` shares the one definition.
(b) `CliRunner.BuildJudge` had no override seam of its own, unlike the composer path's
`composerOverride`, so `--judge` against a non-empty batch was never exercised at the
`CliRunner` level, only through `SemanticJudgeTests` in isolation with hand-built fixtures.
Fixed by adding `judgeOverride`, mirroring `composerOverride`'s existing shape, and a
`CliRunnerTests` case that drives a real record through `--judge` with a controlled completion
client and asserts the eval report's `ActionSem`/`BodySem` tallies reflect the grade.

**D31 addendum, scoped out of the PR #21 review (2026-09-08).** The review's remaining finding
was that `CliRunner`'s per-record batch loop processes independent records sequentially, and
D31's own numbers (92 HTTP requests, batch p95 ~5700 ms against an 18 ms offline baseline) are
the measured cost of a real network call sitting behind that loop. Not fixed: `CountingRetryPolicy`
attributes one call's retries by diffing a single shared counter before and after that call, and
its own comment already states the limit this implies ("a concurrent caller would see another
call's retries mixed in"). Parallelizing the loop before that attribution is made safe under
concurrency would corrupt `CompositionNotes.NetworkRetries` rather than only speed up the
batch, so this is a design change ahead of a review-fix round, not a matter of confidence in
the finding. Scopes: any future concurrency work on the batch loop, which opens by replacing
the shared-counter retry attribution.

**Evidence, PR #21 review round (2026-09-08).** `.\test.ps1`: 443 tests in `Agent.Tests`, 49 in
`Agent.Cli.Tests`, all passing, 100 percent line, branch and method on both modules. Four of
the nine fixes above were additionally verified by reverting each one in isolation and
confirming its new test goes red before reapplying it.


## Latency decisions, proposed for Sprint 7 (2026-09-08)

Written after the step 60 run, from its measurements rather than from preference. One scope
note before them: DESIGN.md section 9 gives Sprint 7 to safety and states (D3). These are
latency, so scheduling them means widening that row or giving them a sprint of their own, and
that scheduling call is not taken here. A task citing any of these is not scheduled until it is.

**D32. Measure a successful call before tuning anything (2026-09-08).** Question: which of the
levers below is worth pulling, given that this project has never observed a successful model
call. Options: tune from the failure path's arithmetic alone; or measure what one successful
completion costs in wall-clock first, and tune against that number. Recommendation: measure
first. The 5.7 seconds a record spends on the model path is entirely failure cost, four attempts
capped at 1000 ms plus two backoffs, and every threshold below is arithmetic about a number
nobody in this project has: how long a completion of this size actually takes. A tuning decision
taken without it is a guess with a decimal point. Scopes: D33 to D36; any Sprint 7 latency task.
Evidence: the step 60 run in DESIGN.md section 9, 23 records, 46 model calls, 92 HTTP attempts,
zero successes. Assumption: A15.

**D33. What a timeout is allowed to cost (2026-09-08).** Question: the SDK's retry policy treats
a timeout as transient and retries it, so a call that was too slow is made a second time inside
the same budget. Options: keep the default; or retry only the transient statuses the vendor
names (408, 429, 5xx) and never a timeout. Recommendation: the second, on two measurements. A
timeout says the completion did not fit the budget, and an identical second call inside the same
budget has no mechanism by which it would fit; it costs one attempt plus one backoff, about 2.8
of the 5.7 seconds. And it is not free: D31's addendum measured that about a third of abandoned
attempts complete server-side and are billed in full, so a futile retry is a paid futile retry.
Scopes: `OpenAiCompletionClient`'s retry policy, `CountingRetryPolicy`, and D35, which only
exists because retries are counted into the budget. Evidence: the step 60 logs, four
timeout-terminated attempts per record; the usage page, about 30 billable requests from 92
attempts. Assumption: A15.

**D34. What the compose-validate loop retries (2026-09-08).** Question: `ValidatingMessageComposer`
composes a second time after any failure, including a transport failure. Options: keep it; or
retry only a safety rejection, where the second attempt carries the rejection reason and can
plausibly do better, and go straight to the fallback on a transport failure. Recommendation: the
second. The corrective retry exists to feed a violation reason back into the prompt (playbook
step 56); a timeout is not a content problem, so re-prompting is delay with no mechanism behind
it, and it is the other half of the 5.7 seconds. The fallback composer is deterministic and
always available, so the record still gets a message either way. Scopes:
`ValidatingMessageComposer`, and `CompositionNotes.Attempts`, which would read 2 rather than 3
on a transport-failed record; that changes a pinned test and how a diagnostics row reads.
Evidence: the step 60 run, `attempts` 3 on every one of 23 records, two of the three failing for
transport reasons. Assumption: A18.

**D35. What one attempt is allowed to take, once a timeout is not retried (2026-09-08).**
Question: D28 divides the stated budget by 1 + `MaxRetries` so that a call and its retry both
fit inside it. If timeouts are not retried (D33), that division buys nothing for the case that
actually happens, and it halves the time the one attempt that matters is given. Options: keep
dividing; give one attempt the whole budget and let the transient-status retry path exceed it,
stated rather than hidden; or divide only for the failure classes that are retried.
Recommendation: none taken, because it needs D32's measurement. If a successful completion lands
under 2000 ms, the second option roughly doubles the chance of success at no cost to the common
path; if it lands over 2000 ms, no division scheme helps and D36 is the real question. Scopes:
`OpenAiCompletionClient.PerAttemptTimeout`, D28 and its addendum. Evidence: none yet, which is
the point of D32.

**D36. What `p95_latency_ms` is (2026-09-08).** Question: D28 reads the records' stated threshold
as a bound on the model call; D31's third way out reads it as a reporting threshold that the
scorer measures and nothing enforces. Options: a bound, as today; a reporting threshold, with the
call timeout configured separately; or a bound with a documented override for comparison runs,
which is D31's second way out. Recommendation: not taken here. It needs D32 and the requester,
because it decides whether this product can use a model at all on these records, and each option
costs something different: as a bound with these thresholds the model answers nothing, which the
step 60 run measured at 0 of 23; as a reporting threshold the product can use the model and the
p95 check simply fails and says so, which is honest but means shipping a configuration that
misses a stated threshold on purpose; the override keeps both and adds a flag whose only user is
an evaluation run. Scopes: D28, D31, `ModelCallBudget`, the CLI. Evidence: the step 60 run.

**D37. Batch concurrency (2026-09-08).** Question: `CliRunner`'s batch loop is sequential, so a
batch's wall-clock is the sum of its per-record latencies, and with a model in the path that is
seconds per record rather than milliseconds. Options: keep it sequential; or run records under
bounded concurrency. Recommendation: worth doing, after D32, and the first prerequisite is
already stated in the D31 addendum, which scoped this same finding out of the PR #21 review:
the retry attribution has to stop diffing one shared counter before it can be made concurrent.
This decision adds a second prerequisite that addendum does not name: D14 pairs output rows to
input records by position, and the per-record log scope assumes one record at a time, so both
have to survive out-of-order completion. Scopes: `CliRunner`'s batch loop, `CountingRetryPolicy`,
D14. Evidence: the D31 addendum's measurement of retries reported as 2 and 0 for two concurrent
calls; the step 60 run's 63 seconds of wall-clock for 12 records.

**Not a lever, recorded so it is not proposed again.** Prompt caching does not apply here.
Automatic caching engages above a prompt-prefix threshold these prompts do not reach, about 430
tokens against roughly 1024, and the vendor's usage page reports a 0 percent hit rate for this
project. The system prompt being identical on every record is what makes it look promising, and
the length is what rules it out. If prompts grow past the threshold, this becomes a real lever
and the numbers should be checked again.

## Sprint 7 decisions, safety and states (2026-09-09)

Playbook steps 61 to 71. The latency decisions above (D32 to D37) keep their numbers and their
scheduling call is still not taken; these are the decisions Sprint 7 actually implements.

**D38. One result per check, never one boolean (2026-09-09).** Question: step 61 says every
constraint the domain imposes becomes its own validator with its own result, never one boolean.
`SafetyValidationResult` is a flat `IReadOnlyList<string>` plus `FairHousingCheckPassed`, and
that flag is computed as `violations.Count == 0`. Options: keep the flat list and let each call
site re-derive what failed by reading the strings; or return one named result per check.
Recommendation: one named result per check. The current derivation is not merely coarse, it is
wrong, and nothing in the suite catches it: a message that merely omits its opt-out line reports
`fair_housing_check_passed: false`, so the diagnostics record a fair-housing failure that never
happened, and A14 says a state is earned by the step that proves it. A second defect the flat
list hides: `FindWholeWord` is `FirstOrDefault`, so a message matching six protected-class terms
emits exactly one violation, and `safety_violations_max` is scored against that count. Scopes:
`SafetyValidationResult`, `SafetyValidator`, `AgentDiagnostics.FairHousingCheckPassed`,
`ValidatingMessageComposer`'s rejection feedback, the review queue of D43, and the evaluator's
Safety check, which reads only the count and so changes verdict on any message with more than
one match. Shape: four checks, `SafetyCheck.OptOutInstructions`, `SafetyCheck.SocialSecurityNumber`,
`SafetyCheck.LongDigitRun` and `SafetyCheck.FairHousing`, each carrying a verdict of `Passed`,
`Failed` or `NotApplicable` and its own detail lines; `NotApplicable` is a check the record did
not require, which is not a pass, the same rule A15 states for the scorer. The names are the
checks themselves rather than one PII check, because D40 gives two of them different answers to
the question of whether a record may switch them off. Evidence: the probe run recorded under
D41. Assumptions: A14, A15.

**D39. Which checks gate and which only report (2026-09-09).** Question: step 62 asks, for each
validator, whether it is a hard gate, a soft flag for review, or a diagnostic, with the reason
written down. Options: classify per check in code, with a disposition field on the result; or
classify in this paragraph and let the structure carry it. Recommendation: the second. All four
safety checks of D38 are hard gates: a failure suppresses the message, which is today's
behavior and stays it, because each maps to legal exposure rather than to taste. Fair housing is
the Fair Housing Act; the opt-out instruction is the revocation-of-consent requirement that
makes a message lawful to send; both identifier checks are a data leak into a channel the
recipient does not control. Brand style (D42) is the one check that is a diagnostic and never
suppresses, because an off-voice message is off-voice, not unlawful. Because every safety check
is a gate and the one non-gate is a different component, the result carries no disposition
field: a field with one inhabited value is speculative code (LC), and the classification lives
here where step 62 asks for it. Scopes: `SafetyValidator`, `BrandStyleValidator`,
`LeasingMessageAgent`'s suppression branch. Evidence: step 62; the four checks of D38.

**D40. Which checks a record cannot switch off (2026-09-09).** Question: step 65 says never let
a per-record flag disable a check that the law or the domain does not allow to be disabled, and
document which checks are unconditional and why. Today `no_pii_leak: false`, or its absence
under D1, turns off both identifier patterns, so a message carrying a literal Social Security
number validates clean. Options: leave both gated and write down why that is acceptable; make
both unconditional; or split them. Recommendation: split them, confirmed by the requester on
2026-09-09. `SocialSecurityNumber` becomes unconditional: no leasing message legitimately
carries one, so there is no case the flag would be protecting, and a record that says
`no_pii_leak: false` is saying it does not need the heuristic, not that it consents to a leak.
`LongDigitRun` stays gated, because it is a proxy that also matches a confirmation number or a
tour reference (D41, case 26), and a record with a legitimate long identifier needs a way to say
so. `FairHousing` was already unconditional and stays it, for the reason already on
`SafetyValidator`: fair housing law has no per-case opt-out. `OptOutInstructions` stays gated on
`include_opt_out_instructions`, because transactional exemptions are real and the record is the
only thing that knows whether this message is one. Scopes: `SafetyValidator`,
`CaseConstraintsExtensions`. This reverses one recorded behavior:
`Validate_PiiCheckNotRequired_LeakedIdentifierIsNotAViolation` asserted that a Social Security
number passes when the flag is off, and that test is inverted here deliberately rather than
deleted, so the contract change is visible in the diff. Assumption: A1 is not involved; this
depends on A17 only for what an absent constraint means. Evidence: step 65; D1.

**D41. What the proxy normalizes and what it exempts (2026-09-09).** Question: step 63 says
keyword and pattern validators are proxies, say so in the code, add an allow-list for the
legitimate uses the pattern would catch, and test both directions. Options: leave the patterns
as they are and widen the scope-out in CODE_REVIEW.md; or fix the cases a run proves are wrong
and scope out the rest with the run as evidence. Recommendation: the second, on a probe that
executed all 37 candidate inputs through the exact patterns as written, on the same .NET regex
engine. What it found, and what is fixed here:

- The Equal Housing Opportunity disclosure, the sentence a compliant leasing message is
  expected to carry, matches six terms and is suppressed. That is the proxy blocking the
  compliant message and passing nothing in its place, and it is the single most important
  finding of the run. Fixed by an exempt-span list.
- `families-only` with a hyphen, and `families  only` with two spaces, both miss. Neither needs
  an adversary: a model composer writes both as ordinary prose. Fixed by normalization.
- A zero-width space inside a term makes it miss. Fixed by stripping format characters, which
  arrive by copy and paste, not only by attack.
- `STOP` inside a URL path satisfies the opt-out check, so a message with no opt-out instruction
  a recipient can act on certifies as having one, and because `OptOutInstructions` is the one
  definition the scorer uses too (D13 b), the false pass propagates into the scorecard. Fixed by
  removing URL spans before the keyword scan.
- A 14-digit confirmation number matches the long-digit run. Fixed by an exempt span, not by
  loosening the pattern.
- A Social Security number written with spaces, and one written bare as nine digits, both miss.
  Fixed by widening that one pattern.

Normalization is applied to a copy of the text used for term matching only: strip U+200B,
U+200C, U+200D and U+FEFF; fold the four unicode hyphens the way `OptOutInstructions` already
folds them; then replace hyphens with spaces and collapse whitespace runs. The allow-list is a
span list, never a term list, so `disability` and `color` stay live terms that still fire
elsewhere in the same message; both directions are tested per row. Deliberately not fixed, with
the run as the evidence rather than an opinion: semantic paraphrase, which is the scope-out
already recorded in CODE_REVIEW.md and needs understanding rather than a pattern; letter spacing
and interior punctuation, because matching across arbitrary separators would make `color` fire
on unrelated letter sequences and trades these misses for a larger false-positive class; and
Cyrillic homoglyphs, because the text under validation is written by this system's own composer,
not by an adversary who controls the bytes. Scopes: `SafetyValidator`, `OptOutInstructions`, a
new normalization helper, CODE_REVIEW.md. Evidence: the probe run of 2026-09-09, 37 inputs,
executed rather than reasoned about. Assumption: A18 for the threat model of the last item.

**D42. What earns a state, and what brand style is (2026-09-09).** Question: step 66 says earn
every state the output claims, nothing hardcoded true, and if a claimed state has no check
behind it, delete the claim or build the check. `brand_style_applied` is the literal `true` in
`LeasingMessageAgent`, and `assertions.required_states` is parsed and then read by nothing at
all, so a record asserting a state this program has never heard of is answered with silence.
Options: delete the claim, since no sample proves what brand style is; or build a check from
what the two samples do prove and record the rest as not earned. Recommendation: build the
check, because `required_states` names `brand_style_applied` in both samples and deleting a
state the input asks for answers the record by ignoring it. Two parts:

First, the states map. Every name in the record's own `required_states` gets a verdict in the
diagnostics: `consent_verified` from the consent gate having run, `fair_housing_check_passed`
from the `FairHousing` check of D38 alone rather than from every check (which is the defect D38
names), `brand_style_applied` from the brand-style validator below, and any other name recorded
as not earned, by name, which is A14's rule made real. The hold-out's `renewal_offer_loaded` is
exactly such a name, and it stays not earned: inventing a rule for it from the record that
carries `renewal_offer_id` would be fitting a rule to the hold-out, which D9 and A19 forbid. Not
earned is the honest answer and it is the answer the assumption already committed to.

Second, what brand style is. Three rules, each satisfied by both sample bodies and by the
template composer on sms and email in both language sets: the opt-out instruction sits on the
body's last non-blank line (sample 1 `Reply STOP to opt out.`, sample 2 `To opt out of emails,
click here or reply STOP.`), the body carries at most one exclamation mark (sample 1 one, sample
2 none), and a subject is present exactly when the channel is email (sample 1 sms null, sample
2 email 59 characters). The keyword half of the first rule calls `OptOutInstructions`, so the
two definitions cannot drift; what it adds over the existing opt-out check is position, which
`Evaluator.OptOut` and `SafetyValidator` do not assert.

What was considered and rejected, with the reason, because each is a rule someone will propose
again. A cap on sms length fails sample 1 outright at 166 characters. A sentence count is 5 and
3, so any interval containing both is chosen rather than observed. "Exactly one call to action"
has no text-level definition both samples satisfy: both carry two imperative asks for one
`cta.type`. "The body carries the first name" and "the message carries an opt-out" are
`Evaluator`'s personalization and opt-out checks restated, and a brand rule that restates an
existing check earns nothing. A second-person pronoun rule passes both samples and fails the
Spanish template output, which has no `you` token, so it is an English rule wearing a general
name. Most instructive: "no ALL-CAPS word other than STOP" passes both samples and is a real
brand property, and it is rejected because the template composer fails it on sample 1's own
record, emitting `TX` from the record's `city_interest` of "Richardson, TX"; flagging a state
abbreviation the record itself supplied is a false positive, not a finding, and the two
formulations that would exclude it fit the samples equally, which makes it an open question
rather than a rule (VF).

What this check does and does not buy, stated rather than left to be discovered: all three rules
pass the template composer by construction, so `brand_style_applied` reads true on every record
of every documented run and no tally moves. It is not therefore the hardcoded `true` it
replaces: it is computed from the message, a test proves each rule can fail, and the composer it
has teeth against is the model path, whose subject, punctuation and closing line are the model's
to get wrong. This is the same disclosure D13 a makes about personalization, and it belongs
beside it rather than inside a claim that the state is now proven. D39 classifies it: it is a
diagnostic and never suppresses, because an off-voice message is off-voice and not unlawful.
Scopes: a new `BrandStyleValidator`, `AgentDiagnostics`, `LeasingMessageAgent`, CODE_REVIEW.md.
Evidence: the two sample bodies measured character by character on 2026-09-09, and the template
composer's own output for both channels and both language sets. Assumptions: A12, A14.

**D43. Where a suppressed draft goes (2026-09-09).** Question: step 67 says that on a final
validation failure the program emits a review-queue record rather than silently dropping output,
because suppression is a business decision and has to be surfaced. Today a safety suppression
writes the `none` message, sets `suppression_reason`, and discards the draft text entirely, so
no human can ever see what was rejected or why; and the diagnostics that carry the reason are
written only when `--diagnostics` is passed. Options: put the draft in the diagnostics file; or
give the queue its own output. Recommendation: its own output, `--review-queue <path>`, one row
per record suppressed by the safety gate, carrying the task id, the channel, every violation by
check, and the rejected draft. The diagnostics file is a full per-record dump of how every
decision was reached and is read when debugging a run; the review queue is a work list, it is
empty on a healthy run, and its length is the number this sprint's step 71 has to report.
Consent suppression is not in it: not contactable is the correct decision, not a failure.
Composition failure is not in it either, for now, because there is no draft to review. Scopes:
`CliRunner`, a new writer, OPERATIONS.md. The queue carries prospect text by design, which is
the one place in this program that is true: it is a file a reviewer opens, not a log line, and
step 68's redaction rule is about logs. Evidence: step 67. Assumption: A2.

**D44. Whether the vendor's retention default is acceptable (2026-09-09).** Question: step 69
asks for the data retention of every external service that receives user data, and for the
production path if the default is not acceptable. Read from OpenAI's own platform data page on
2026-09-09: API data is not used to train models by default, and abuse-monitoring logs for
`/v1/chat/completions` are retained up to 30 days. Options: accept the default; take one of the
vendor's two approval-gated controls; or stop sending prospect data. Recommendation: the default
is acceptable for this project as it stands and is not acceptable for a production deployment
carrying real prospect data, and both halves are stated rather than the convenient one. It is
acceptable here because the evaluation sets are synthetic and because of what the prompt
actually contains: first name, property name, stated interest, persona, lifecycle stage,
language, and two dates. It carries no phone number, no email address, no unit number, no
renewal offer id, and no free-text note, and that is a property of `BuildUserPrompt` a golden
test already pins. The production path, if a real deployment sends real prospect data, is one of
the two controls the same page names, Modified Abuse Monitoring or Zero Data Retention; both
exclude customer content from the abuse-monitoring logs, both require prior approval by OpenAI
and additional terms, and `/v1/chat/completions` is on the eligible list, with the only side
effect on that endpoint being a forced `store=false` that a single-shot completion does not
rely on. What the vendor's documentation does not address, stated as absence rather than filled
in: what happens to a request the client abandons at its timeout while the server completes it,
which is the case D31 measured at roughly a third of attempts. Nothing published narrows or
extends the 30-day window for a dropped connection. Scopes: DESIGN.md section 8. Evidence:
developers.openai.com/api/docs/guides/your-data, read 2026-09-09; the step 60 run and D31.

**D45. What the widened Social Security pattern is allowed to match (2026-09-09).** Question:
D41 widened the Social Security pattern so `123 45 6789` and a bare `123456789` match, and the
implementation of that row, `\b\d{3}[- ]?\d{2}[- ]?\d{4}\b`, also matches a ZIP+4. It matches
`75201-1234` by reading the separator as optional in the first position and present in the
second, so a message carrying a property address is suppressed by an unconditional gate that
D40 made impossible for a record to switch off. The sprint that widened the pattern introduced
the false positive, and step 71 asks for zero false-positive suppressions, so it does not ship
as a pinned defect. Options: revert the widening, so the two forms the probe proved missing go
back to missing; add a ZIP+4 exemption span; or require the pattern's grouping to be
consistent. Recommendation: consistent grouping, as three explicit alternatives, hyphens
throughout, spaces throughout, or nine bare digits. A ZIP+4 is a five-digit group and a
four-digit group, which none of the three describes, so it stops matching by construction
rather than by an exemption that has to be maintained. Reverting was rejected because the
missing forms are a real leak of the exact identifier the check exists to catch, which is what
D41 recorded; an exemption span was rejected because it answers one spelling of the wrong shape
while `12345-6789` and every other mis-grouped pair stay matched. Second half: the
confirmation-and-reference exempt span D41 gave the long-digit run is applied to this check
too, because a bare nine-digit confirmation number is the same false positive as the fourteen
digit one and the span already exists; leaving it on one check and not the other would be the
inconsistency, not the fix. Scopes: `SafetyValidator.SocialSecurityNumberPattern` and
`SocialSecurityNumberCheck`. Evidence: both spellings executed against the pattern on
2026-09-09; `Ssn_ZipPlusFour_IsAKnownFalsePositive`, the test that recorded the defect, is
inverted to assert it is not a violation, so the fix is visible in the diff the way D40's
inversion is. Assumption: A17 is not involved; this check is unconditional under D40.

**D46. What an exception is allowed to say, and who says a missing member's name (2026-09-09).**
Question: step 68 says never log a prompt, a raw model response, or a secret above debug level.
An audit of every log call site found the leak is not in any message template: it is
`LogLineFormatter` appending the whole `Exception.ToString()`, so a `ClientResultException`
renders the vendor's raw error response body and a `JsonException` renders the offending
character of whatever it was parsing, both at Warning, to stderr and to `--log-file`. Options:
redact inside `ToDiagnosticString`, which every failure row already uses; or add a second
function and choose per call site. Recommendation: the second. `ToDiagnosticString` has four
remaining callers and every one of them reports an exception whose message this program or the
operating system wrote, an unopenable `--log-file`, an unknown `--composer`, a missing API key,
and a per-record bug; redacting those degrades the stderr usage rows and buys no safety.
`ToRedactedDiagnosticString` reports a type name, and for the two types that carry content a
bounded locator instead of the content: an HTTP status for `ClientResultException`, a line and
byte position for `JsonException`, never `Message` and never `Path`. It is used exactly where
the message can carry vendor, model, or record text. Two rows deliberately change what an
operator reads: the per-record parse failure and the `--replay` file-format failure now name a
position rather than the parser's prose, which is accepted because stderr is where the console
provider renders and is therefore a log stream. Scopes: `ExceptionFormatting`,
`OpenAiMessageComposer`, `SemanticJudge`, `LenientExpectedOutcomeConverter`, both readers,
`IngestNotes`, `CliRunner`'s ingest line. Evidence: a real `ClientResultException` built through
the SDK over `FakeHttpMessageHandler`, asserted to carry the vendor body in `ex.Message` before
being asserted absent from the rendered line, with a negative control proving the assertions are
not vacuous. Assumption: A16 for the unknown-member count.

Addendum, the one piece of new logic. Redacting the reader's message broke a stated contract:
D1 promises that a line missing a required member produces an error row naming the member, and
that name came only from the deserializer's prose. `JsonlRecordReader` now states it from its
own `RequiredMembers` constants, which is program-authored text rather than record text. This
re-establishes D1 rather than adding anything, and it is recorded because a reviewer reading
the diff would otherwise see a redaction commit growing a feature.

Two findings the audit raised and the implementation disproved, recorded so neither is chased
again. The `expected` block's parse error does not leak label text: that converter only ever
sees a well-formed JSON value, because a malformed one fails the outer record read first, and a
well-formed value's exception carries only declared type and property names plus a position.
What did reach the log was the full `ToString()`, twenty or more stack frames with absolute
local source paths, which is why the change was made anyway. And `ReportFailure` passing an
already-interpolated string as a message template is not the format-injection bug it looked
like: with no arguments, `FormattedLogValues` never builds a formatter and returns the string
verbatim, proved by probing five brace shapes through the console provider, so it was left
alone rather than fixed for a case that cannot occur.

**D47. A null inside a list the element type says cannot hold one (2026-09-09).** Question: D42
gave `assertions.required_states` its first reader, and a record spelling
`["consent_verified", null]` took the whole record out with an `ArgumentNullException` from the
dictionary indexer. The list is typed `IReadOnlyList<string>`, but
`RespectNullableAnnotations` does not reach inside a collection, so the deserializer honours
the element's non-nullability nowhere and the type is a claim the wire does not keep. This is
the same class of defect as the retrospective's silent year-0001 dates, one level deeper: D1
made every member nullable and stopped at the members. Options: guard at the map only; make the
element type tell the truth and skip an absent name; or reject the record. Recommendation: the
second. A16 says an input shape is never an error, and a null name asserts no state, so there
is nothing to reject and nothing to report: it is skipped, and the names beside it are still
answered. A blank or whitespace name is the same nothing and goes the same way, through
`Presence.IsAbsent`, which is already this program's one definition of a stated-but-empty
value. Scopes: `CaseAssertions.RequiredStates`, `RequiredStateMap.For`. Evidence: the record
above run through the CLI, which exited 2 with a bare `ArgumentNullException` and no output row
for that record before the change and exits 0 with its message after. What this does not do,
recorded so the gap is visible rather than assumed closed: it fixes the one list D42 gave a
reader, not every collection in the record types. The general rule, that a value-type or
non-nullable element inside a collection defaults or nulls silently, is now a known shape and
the next collection to gain a reader inherits it. Assumptions: A16, A17.

**D43 addendum (2026-09-09).** Two differences from the paragraph above, both found by building
it. First, the entry carries no channel of its own: D43's sentence lists one, but the draft
already carries it, and two spellings of one fact are what a `with` copy desynchronizes, so a
reader takes it from `draft.channel`. Second, and not cosmetic, the queue as specified could
never have had a row in it: see D48.

**D48. The composer seam carries a refusal, not just a failure (2026-09-09).** Question: D43's
queue was written, wired and empty, and the reason is a defect three layers deep that one run
exposed. A record whose own `city_interest` reads `families only` has that text written into
its body by the template composer, so the violation comes from the record's data and the
fallback reproduces it exactly. `ValidatingMessageComposer` then refuses both attempts and the
fallback, and returns `Result<ComposedMessage>.Failure(string)`, which has no payload. Three
things follow, and all three were measured rather than reasoned about. The rejected draft is
destroyed before anything can queue it. `SuppressionReason.SafetyViolation` is unreachable
under production wiring, because `CliRunner` always wraps the composer, so the orchestrator's
own step 5 gate never sees a violating message and the queue is structurally empty rather than
empty on a healthy run. And the record reports `fair_housing_check_passed: not_evaluated`
although its message did contain steering language, which is worse than the defect D38 fixed,
because `not_evaluated` has to mean nothing was ever checked.

Options: descope the queue and record the finding; widen the queue to composition failures,
which are reachable but carry no draft and no violations, so most of the value is gone; have
the compose loop stop being a gate and return its best attempt for the orchestrator to refuse,
which makes a composer knowingly hand back unsafe content; or have the loop's refusal carry the
draft out alongside the failure. Recommendation: the last, chosen by the requester on
2026-09-09. Both gates stay where they are and nothing unsafe ships: the loop still refuses,
it just stops destroying the evidence.

The seam gains its own result, `ComposeOutcome`, with three cases rather than two:
`Composed` is a message to send, `Failed` is no draft at all (a transport error, a malformed
completion), and `Refused` is a draft that exists and was refused on safety. `Result<T>` is not
changed to carry a payload: it is a general type in `Agent.Common` and the thing being carried
is a composition concept, so it belongs on the composition seam and nowhere else.

`Refused` carries the draft and not the violations, deliberately. The orchestrator re-derives
them with its own validator, which is the step 5 gate it already has and which until now no
production record could reach. That keeps one source for one fact, and it means the fix for the
third defect falls out of the fix for the first: a refused draft flows down the existing
`hasViolations` branch, so `suppression_reason` reads `safety_violation`,
`fair_housing_check_passed` reads the FairHousing check's real verdict, and the queue gets its
row, all from code that was already written and previously unreachable. Scopes:
`IMessageComposer` and its three implementations, `LeasingMessageAgent`, `CliRunner`, the test
fakes. Evidence: the steering record above run through the real CLI, whose diagnostics read
`composition_failed` and `not_evaluated` before the change. Assumptions: A12, A18.
