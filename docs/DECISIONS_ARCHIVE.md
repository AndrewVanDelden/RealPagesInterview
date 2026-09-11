# Decisions archive

Every decision paragraph this project has taken, in full and verbatim as it was written on the
day it was taken. `DECISION_LOG.md` carries the current phase and one paragraph per sprint; the
paragraphs those summaries stand for are here. A citation of the form D followed by a number
resolves here by searching for its bold heading, and an addendum sits with the paragraph it
amends rather than in the order it was written, so one search finds a decision and everything
that has amended it. Paragraphs arrive here as written and are not rewritten (D68).

One paragraph per decision, in the recording form of `~/.agent-rules/ARCHITECTURE.md`: the
question, the options, the recommendation, what it scopes, the evidence, the numbered assumption
it depends on (assumptions are the table in [DESIGN.md](DESIGN.md) section 7). A task that
cannot cite a paragraph here is not scheduled. Dates are when the decision was taken; D1 to D8
were first written in the retrospective of 2026-09-06 and are restated here under the frame D9
sets.

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

**D3 addendum, PR #21 review (2026-09-08).** D3's rule that a suppressed record's diagnostics
carry no composer identity had a third case unhandled: `AgentDiagnostics` is built from the
compose step's notes before the final safety check runs, and the final-safety-suppression
branch returned that same object unchanged, so a record with no message on the wire could
still carry a non-null `composition`, naming a composer as if its draft had shipped. The other
two suppression cases (consent, composition failure) already null the field. Fixed by nulling
`Composition` on that branch too. A test asserting the null now sits beside the existing
assertions on `RunAsync_FinalSafetyValidationFindsViolations_SuppressesMessage`, confirmed to
fail against the prior code.

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

**D25 addendum, PR review (2026-09-08).** The catalog shipped with an sms option list on
every row, and after D26 moved the prose into the language sets nothing read it: the option
text a message carries comes from `MessageTemplates.SmsOptions`, keyed on the call-to-action
type, while the catalog's copy was keyed on `primary_cta` and could drift from it with nothing
to catch the drift. Flagged in a cold-context review of the sprint diff. The column is removed;
`CallToAction` is now the type and the link path, and the header comment no longer claims to
own an option list it does not. The rule that survives: the catalog owns what a call to action
is and where its link points, and the language sets own every word a person reads (LC, HSC).

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

**D26 addendum, PR #21 review (2026-09-08).** `MessageTemplateCatalog.Resolve`, D26's own
mechanism, restated the BCP-47 primary-subtag split `LanguageDetector.TryParseTag` (D13 c)
already implements, independently, in a different namespace. Fixed by extracting the split
into `Agent.Common.Bcp47.PrimarySubtag`, called from both; no behavior changed, since both
sites matched the same two tags the same way, only the split itself moved to one place.

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
Superseded 2026-09-10 by D33 and D35: a timeout is no longer retried, so the division bought
nothing and is gone, and one attempt is given the whole budget; the retry after a transient
status may now exceed it, which `OpenAiCompletionClient`'s constructor states.

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
the shared-counter retry attribution. Resolved 2026-09-10 by D37, which replaced that
attribution first and then made the loop concurrent.

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
attempts. Assumption: A15. Taken 2026-09-10 on the requester's "dont defer do now", the second
option. `CountingRetryPolicy` overrides `ClientRetryPolicy.ShouldRetryAsync`, whose signature was
confirmed by reflection over the restored System.ClientModel 1.14.0: an attempt that ended in an
exception is never retried, and a response is retried only when
`PipelineMessageClassifier.Default` calls its status transient, which the package's own docs and
a probe through a fake transport both give as 408, 429, 500, 502, 503 and 504, the base still
capping it at `MaxRetries`. Read literally, "never a timeout" became "never an exception", so a
request that got no response at all is not retried either, by the requester's acceptance; that
also closed a gap in which two such failures reached the composer as an `AggregateException` its
catch list does not name, and the record became an ERROR row. A timeout is one HTTP attempt and
arrives as one `TaskCanceledException`, so `IsTimeout`'s `AggregateException` branch went.
Test-first: the timeout test saw two attempts and the no-response test an `AggregateException`
before the change; `.\test.ps1` exit 0, 593 and 87 tests, 100 percent on both modules.

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
transport reasons. Assumption: A18. Taken 2026-09-10 on the requester's "dont defer do now", the
second option. In `ValidatingMessageComposer`, an attempt that returns `ComposeOutcome.NoMessage`
(a transport failure, a timeout, a malformed or wrong completion) leaves the loop for the
fallback composer; only a safety-validation rejection is retried, with its violations in the
second prompt. D66's accounting is unchanged: what that attempt spent still rides onto the
outcome. The fallback's `Attempts` stamp is the number of model attempts made plus one, so a
record whose first attempt returned no message reads `attempts` 2 and `model_cost.calls` 1 where
it read 3 and 2; two safety rejections still read 3. The fallback warning now reads `No compose
attempt produced a clean message; falling back to the safe fallback composer.` Evidence: seven
tests in `ValidatingMessageComposerTests.cs` and `LeasingMessageAgentTests.cs`, changed to the new
behavior, failed on the unfixed loop and pass after, and
`ComposeAsync_FirstAttemptFails_RetryReceivesFailureReasonAsCorrection` is replaced by
`ComposeAsync_FirstAttemptReturnsNoMessage_TheFallbackAnswersOnTheSecondCall`, since the retry it
tested no longer exists; `.\test.ps1` exit 0, 594 and 87 tests, 100 percent on both modules.
Older paragraphs below that name the renamed tests record what existed when they were written.

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
the point of D32. Taken 2026-09-10, the second option, on D32's measurement of about 1.5 to 4.5
seconds a completion (the run and debug fact of that date, the first model calls that
answered). `OpenAiCompletionClient` hands the whole call budget to `NetworkTimeout`, and
`PerAttemptTimeout`, which would have become a function returning its input, is removed with the
test that pinned the division. The cost is stated in the constructor's comment: after a
transient status the retry gets the whole budget again, so such a call can take up to twice the
budget plus the SDK's backoff. Test-first: an attempt needing 1200 ms of a 2000 ms budget, and a
429 followed by such an attempt, both timed out at the old 1000 ms attempt before the change;
`.\test.ps1` exit 0, 594 and 87 tests, 100 percent on both modules.

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
Closed 2026-09-10 by D70, which is this decision's third option: the records' threshold still
bounds the call and is still what the p95 check reads, and `--model-call-budget-ms` overrides
the bound for an evaluation run.

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
calls; the step 60 run's 63 seconds of wall-clock for 12 records. Taken 2026-09-10 on the
requester's "dont defer do now", with both prerequisites landed first. Retry attribution:
`CountingRetryPolicy` counts into a holder the client opens per call, carried by an instance
`AsyncLocal`; this is per-call state inside one object, not a static accessor standing in for
constructor injection, so the AGENTS.md rule that reserves the latter for `AgentLog` is not
touched. Proved by two calls in flight on one client, which reported 2 retries for the retried
call before the change and 1 and 0 after. The loop: `CliRunner` runs records through
`Parallel.ForEachAsync` with at most four in flight, a constant, because one record's calls are
sequential and four requests in flight is a margin under a vendor rate limit this project has
never measured; each record's `RecordRun` is written to its input position and folded afterwards,
single-threaded and in input order, into the output, the diagnostics, the review queue, the
scored runs, the batch cost, the failure count, the ERROR row and the stderr line, so D14's
pairing by position holds. D16's scope is opened per record inside its own async flow, still only
in `CliRunner`. Proved by records forced to overlap and finish in reverse input order, which keep
input order in every file, and a fold in completion order was checked to fail that test; by three
records failing while others are in flight, whose log entries each carry their own `TaskId`
alone; and by a run cancelled from inside its first record, which starts no further record. The
template run on `synthetic_12.jsonl` writes a byte-identical `--output` before and after.
`.\test.ps1` exit 0, 595 and 90 tests, 100 percent on both modules.

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

**D43 addendum (2026-09-09).** Two differences from the paragraph above, both found by building
it. First, the entry carries no channel of its own: D43's sentence lists one, but the draft
already carries it, and two spellings of one fact are what a `with` copy desynchronizes, so a
reader takes it from `draft.channel`. Second, and not cosmetic, the queue as specified could
never have had a row in it: see D48.

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

**D49. The introducer span matched inside a longer word (2026-09-09).** Question: a code review
of the Sprint 7 diff (PR #24) found `IntroducedIdentifierSpan`'s alternation,
`(?:confirmation(?:\s+number)?|reference)...`, has no word boundary before it, so `reference`
matches starting inside `preference`. `"Your preference and SSN 123-45-6789 are noted."` strips
to `"Your p  are noted."` before `SocialSecurityNumberPattern` runs, and the unconditional
SocialSecurityNumber gate (D40) passes a message that carries a real, unmasked SSN. Options:
require the alternation to start at a word boundary; or require it to be preceded by
non-word-or-string-start via a lookbehind. Recommendation: the first, `\b` before the group,
which is the direct fix and changes nothing else the span matches. Scopes:
`SafetyValidator.IntroducedIdentifierSpan`. Evidence:
`Ssn_WordReferenceInsideALongerWord_DoesNotExemptTheFollowingNumber`, written failing against
the pattern before the fix and passing after. Assumption: none; this is the same check D40
already made unconditional.

**D50. The disclosure's exempt span crossed a clause it did not own (2026-09-09).** Question:
the same review found two ways `ExemptSpans`' disclosure alternative,
`(?:do|does) not discriminate[^.!?]*`, reads past the disclosure sentence it exists to exempt.
First, greedy `[^.!?]*` does not stop at a comma, so
`"We do not discriminate, but this community is families only."` has the steering clause erased
along with the disclosure, and `FairHousingCheck` reports no violation. Second, a report from
Antigravity (Gemini 3.8 Flash) on the same PR found that `NormalizeForTermMatching` collapses a
newline to a bare space before `ExemptSpans` runs, so
`"We do not discriminate\nThis community is families only."` fuses into one sentence with no
terminator between the disclosure and the steering text, and the same erasure happens with no
comma involved. Both defeat FairHousing, the other unconditional gate D40 names. Options for the
first: bound the span's length the way the confirmation-number span is bounded; require it to
stop before a comma that starts a new clause; or leave it, since the disclosure's own protected-
class list is comma-separated and a bare-comma stop would flag the disclosure itself. Recommendation:
stop before a comma immediately followed by a contrastive conjunction (`but`, `yet`, `however`,
`although`, `though`), which is what introduces a clause the disclosure did not write; an
ordinary comma inside the term list still passes through untouched. Options for the second:
teach `ExemptSpans` about newlines directly; or fix the normalizer that erases the boundary
before `ExemptSpans` ever runs. Recommendation: the second, since the same collapse would defeat
any future exempt span or term match the same way, not only this one. A newline not already
preceded by a sentence terminator is now replaced with `". "` rather than `" "`, so the sentence
boundary a line break represents in a message body survives normalization. Scopes:
`SafetyValidator.ExemptSpans`, `SafetyTextNormalizer.NormalizeForTermMatching`. Evidence:
`Validate_DisclosureFollowedByASteeringClauseInTheSameSentence_StillYieldsTheSteeringViolation`
and `Validate_DisclosureFollowedByASteeringSentenceOnANewLine_StillYieldsTheSteeringViolation`,
both written failing against the code before the fix and passing after. Assumption: none.

**D51. Redaction did not reach the evaluator's own catch (2026-09-09).** Question: the same
review found `Evaluator.Score`'s catch attaches the raw exception to `log.LogError` and writes
`ex.ToDiagnosticString()` into `RecordScore.Unscoreable`, neither of which D46 covers.
`Score` reads the composed message and the record's own labeled content, the same boundary D46
named for the composer and the readers, and `LogLineFormatter` appends `Exception.ToString()` in
full whenever a raw exception is attached, regardless of the message template, so attaching `ex`
bypasses redaction outright the same way the composer's old call did. Options: leave it, since
no exception `Score` currently throws is known to carry record or model content; or bring it
under D46's rule so a future one does not have to be found again. Recommendation: the second,
consistent with the rest of D46: the exception is never attached to the log entry, and
`ToRedactedDiagnosticString` supplies both the log parameter and `RecordScore.ScoringError`.
Scopes: `Evaluator.Score`'s catch. Evidence:
`Evaluate_ScoringThrows_LogsTheExceptionTypeWithoutAttachingTheRawException`, replacing the test
that pinned the old, unredacted behavior (`Evaluate_ScoringThrows_LogsErrorWithTheException`),
written failing against the code before the fix and passing after. Assumption: none.

**D52. The introducer span was computed twice per check (2026-09-09).** Question: the same
review found `SocialSecurityNumberCheck` and `LongDigitRunCheck` each call
`IntroducedIdentifierSpan().Replace(text, " ")` independently, and since SocialSecurityNumber is
unconditional (D40), both run and both strip the same text on any record with `no_pii_leak:
true`. Recommendation: compute the stripped text once in `Validate` and pass it to both checks,
the same pattern `FairHousingCheck` already uses for its own `scannable` variable. No behavior
changed; this is a pure duplication removal. Scopes: `SafetyValidator.Validate`,
`SocialSecurityNumberCheck`, `LongDigitRunCheck`. Evidence: the existing safety-validator suite
(78 tests) unchanged and passing after the refactor. Assumption: none.

**D53. `RequiredMembers` duplicated what `ProspectCase`'s constructor already states
(2026-09-09).** Question: the same review found `JsonlRecordReader.RequiredMembers` hand-spells
the three D1 members a second time, with nothing tying it to `ProspectCase`'s `[JsonConstructor]`
parameters, the thing `RespectRequiredConstructorParameters` actually enforces. A future change
to which members are required could update one and not the other with no test catching the
drift. Recommendation: derive the array by reflecting on the `[JsonConstructor]`'s parameters and
running each name through `AgentJsonOptions.Default.PropertyNamingPolicy`, the same policy the
serializer itself uses, so the two can never disagree. Scopes: `JsonlRecordReader.RequiredMembers`.
Evidence: the existing reader suite (29 tests) unchanged and passing after the refactor.
Assumption: none.

**D54. `ComposeOutcome.NoMessage`, reconsidered and kept (2026-09-09).** Question: the same
review flagged `NoMessage` as an abstraction extracted at its second occurrence rather than its
third, against this program's own Earned Abstraction rule, and recommended splitting `Failed`
and `Refused` into two independent records with the two call sites in
`ValidatingMessageComposer` pattern-matching each explicitly. That change was made, built, and
then reverted before landing. The two call sites it touches are both places where
`ComposeOutcome.Refused` cannot occur under any composer this program ships: no inner composer
refuses its own draft (only the fallback path, after the loop, can), so a real switch or pattern
match over `Refused` and `Failed` at either site has one arm no test can reach honestly, and
`SequenceMessageComposer`, the one fake that stands in for an inner composer in tests, says so in
its own comment. `ComposeAsync` is also async, and this program's coverage gate excludes
compiler-generated code (`/p:ExcludeByAttribute=CompilerGeneratedAttribute` in `test.ps1`), which
excludes the state machine an async method compiles to; the gate would not have caught the
unreachable arm, but writing it anyway is exactly VF's "never write a branch a test cannot
exercise honestly," done here as reasoning, not tooling. `NoMessage` is not, on reflection, a
pure DRY convenience someone reached for a call early: reading `Failed` and `Refused` as one case
is what lets the loop stay written without that branch, which is a second reason for the type
distinct from the letter of "extract on the third occurrence." Recommendation: no change.
Scopes: none; `ComposeOutcome.cs` and `ValidatingMessageComposer.cs` are unchanged from D48.
Assumption: none.

**D55. The final safety validation runs twice on the same text, kept (2026-09-09).** Question:
the same review found `SafetyValidator.Validate` is called once inside
`ValidatingMessageComposer`'s compose-validate loop and again, unconditionally, in
`LeasingMessageAgent`'s step 5, on text that differs only by `SendAt`, which `Validate` never
reads, and recommended threading the composer's own `SafetyValidationResult` through
`ComposeOutcome` so step 5 could reuse it instead of recomputing. Recommendation: no change.
Step 5's own comment already states why: "this is the orchestrator's own gate, not borrowed
trust in the composer's cooperation." That sentence is D43's design, not an oversight this
review found; reusing the composer's result would make step 5 trust the composer's own
validation exactly where the design says it deliberately does not, for every `IMessageComposer`
that might reach `LeasingMessageAgent` directly in a test or a future caller, not only through
`ValidatingMessageComposer`. The cost is one extra linear pass over the message text per record,
not an unbounded or quadratic one. Scopes: none. Assumption: none.

**D56. `RequiredStateMap.For`'s hardcoded switch, kept (2026-09-09).** Question: the same review
found `RequiredStateMap.For` maps three state names to three positional verdict parameters via a
switch rather than a declared table, and recommended a table so a new required state would be
one row instead of a new parameter and two call-site edits. Recommendation: no change. D42
already closed the required-state set at three names and argued at length against inventing
verdict logic for a name like `renewal_offer_loaded` without the evidence D9 and A19 require;
each of the three states is also computed differently at its two call sites (one path passes
real computed verdicts, the other stubs two of three as `NotEvaluated`), so a table would still
need per-call-site wiring and would not remove the touch points the finding is concerned about.
Scopes: none. Assumption: none.

## Sprint 8 decisions, structure and narration (2026-09-09)

Playbook steps 72 to 79. These are the decisions Sprint 8 implements, and they are numbered from
D57 because D49 to D56 were already taken by the review-fix decisions that landed with PR #24 on
the same day.

**Run and debug fact, the sprint's own citations and comments corrected (2026-09-09).** The code
half of this sprint left comments citing decision numbers that name a different decision, and
comments describing a component D57 deleted. Both are now corrected in place, comment text only,
with no behavior, signature or test changed. An earlier version of this paragraph recorded the
first three citations as noted here rather than fixed, on the ground that the documentation half
of the sprint does not edit `src/` or `tests/`; that is no longer what happened.

Three citations were repointed from D49 to D57. D49 is the introducer-span fix, a different
decision entirely; D57, below, is the consent gate merged into the channel selector. The three
are `src/Agent/Orchestration/LeasingMessageAgent.cs` lines 13 and 30, and
`tests/Agent.Tests/Decisions/ChannelSelectorTests.cs` line 71.

Six were repointed from D43 to D48. D43 decides where a draft the final safety gate suppressed
goes, which is the review queue; D48 decides that the composer seam returns `ComposeOutcome` and
that a refusal carries its draft out for the orchestrator to validate. The six are
`src/Agent/Composition/ComposeOutcome.cs` line 5,
`src/Agent/Safety/ValidatingMessageComposer.cs` line 14,
`src/Agent/Orchestration/LeasingMessageAgent.cs` line 102,
`tests/Agent.Cli.Tests/CliRunnerTests.cs` line 1066,
`tests/Agent.Tests/Safety/ValidatingMessageComposerTests.cs` line 95, and
`tests/Agent.Tests/Orchestration/LeasingMessageAgentTests.cs` line 539.

One was repointed from D3 to D2, at `src/Agent/Decisions/ActionTypes.cs` line 17. D3 decides the
output contract, what suppression looks like on the wire and what the diagnostics carry; D2 is
the paragraph that puts consent first and states that a record which is not contactable gets
`no_op` with reason `no_contact_consent` and nothing else runs, which is what that comment is
about.

The same pass corrected the comments that still described the consent gate as a component that
exists, or spelled the step 1 suppression as "consent suppression". In `src/`:
`Orchestration/AgentDiagnostics.cs`, where the `ActionPlan` sentence said the consent gate
suppressed the record and three sentences spelled the same state "consent suppression";
`Decisions/ActionTypes.cs`, where `no_op` was said to be emitted by the consent gate;
`Orchestration/ActionPlanNotes.cs` and `Orchestration/RequiredStateVerdict.cs`, which both said
the gate suppressed the record. In `tests/`: three comments in
`Orchestration/AgentDiagnosticsTests.cs`, one in `Orchestration/LeasingMessageAgentTests.cs`, and
one in `Orchestration/RequiredStateMapTests.cs`. In `docs/`: four places in `OPERATIONS.md`, the
`--review-queue` flag row and three of the `--diagnostics` paragraphs, which named the consent
gate or spelled step 1 "consent suppression" in present-tense operator instructions. That file
was missed when this paragraph was first written, which made this paragraph itself an incomplete
record of its own sweep; it is swept now and listed here. Each of the corrected places names what
the code does: the channel selector returns no value, and the agent's own step 1 emits the
`no_op`. Two mentions in `src/` and `tests/` stay, because both are written as history and read
as history: `LeasingMessageAgent.cs` line 13, which says D57 merged the old gate into the
selector, and `ChannelSelectorTests.cs` line 71, which says the gate asked the same question this
selector answers. In `docs/`, `DESIGN.md` lines 356 and 370 stay for the same reason: both sit
inside the
dated "Numbers after Sprint 4" and "Numbers after Sprint 5" paragraphs, which record what the
code did on those dates. Evidence: `dotnet build` clean and `.\test.ps1` exit code 0 with 551 and
61 tests and 100 percent line, branch and method coverage, unmoved from the code half of the
sprint.

**Run and debug fact, four review findings on the Sprint 8 prose fixed (2026-09-09).** A review
of the uncommitted Sprint 8 work found four defects, every one of them in prose. All four are
corrected here, with no behavior, signature or test changed.

The first was false rather than merely incomplete. The justification on `LeasingMessageAgent`'s
`ConsentVerified` constant said the selector reads consent to answer "which channel", so consent
has been verified by the time either path runs, whichever way it answered.
`ChannelSelector.Select` reads consent only inside its `foreach`, so `channel_preferences: []`
makes zero consent reads, returns no value, and the record still records
`consent_verified: earned`. That input is live:
`ChannelSelectorTests.Select_EmptyChannelPreferencesDespiteFullConsent_ReturnsNone` is exactly
it, and
`LeasingMessageAgentTests.RunAsync_OnlyRequiredMembersAndNoConsent_SuppressesAndAssertsNoState`
feeds it to a real agent. The output was never wrong and the deleted `ConsentGate` returned
`ConsentVerified: true` on that path too, so the verdict is unchanged and only its stated reason
is: `consent_verified` is earned by a record reaching step 1 at all, the consent-driven
selection is the step that owns the state, and no input makes the verdict anything else. The
claim was restated in six places and all six now say that: `LeasingMessageAgent.cs`,
`LeasingMessageAgentTests.cs`, D57 below, the A14 row of DESIGN.md section 7, DESIGN.md section
9's states-map paragraph, and NARRATION.md's step 1. A14's own statement is left as it stands,
and is recorded here as reading, for this one state, as "the step that owns it ran" rather than
"the step proved it".

The second was `OPERATIONS.md` still naming the deleted gate in present-tense operator
instructions, in four places; that sweep is recorded in the paragraph above.

The third was README's latency claim, stated as 22, 20 and 19 ms with no caveat where DESIGN.md
section 9 already discloses that the figure is wall clock, is not pinned in the suite and moves
between runs. Three further runs of the same three commands read 21, 22 and 19; 21, 21 and 20;
and 23, 22 and 20. README now carries the disclosure and states what does reproduce: on each set
one record pays the one-time just-in-time compilation cost and reads around 20 ms, which is that
set's p95, every other record reads single-digit milliseconds, and runs on unchanged code have
read p95s from 19 ms to 23 ms. The per-check tallies are untouched: they reproduced exactly.

The fourth was README's seam arithmetic: three components behind an interface and "the other
six" removed by D58 leaves a reader at nine where ten existed, because the seventh removal is
`IConsentGate` under D57. The paragraph now names both decisions, and says the completion client
has no box of its own in the diagram above it, since only Compose and Validate are boxes.

Evidence: `dotnet build` clean; `.\test.ps1` exit code 0 with 551 and 61 tests and 100 percent
line, branch and method coverage; `.\check-instruction-files.ps1` clean; and the three
documented runs, every per-check tally identical to the numbers already in DESIGN.md section 9,
exit codes 0, 0 and 2.

**D57. The consent gate merged into the channel selector (2026-09-09).** Question: whether
"is this record contactable" and "on which channel" are two decisions or one. Options: keep the
two components, one answering contactability and one answering the channel, and keep the
orchestrator calling them in order; or delete the gate and let the selector's absence of a value
be the answer to both. Recommendation: the second. The two components computed the same
predicate over the same two inputs: `ConsentGate.Evaluate` computed
`channelPreferences.Any(consent.IsOptedIn)` and `ChannelSelector.Select` computed the first
channel satisfying `consent.IsOptedIn`, so the gate's answer was already implied by whether the
selector found one. A question answered twice is a question that can be answered two ways, and
the orchestrator paid for the second answer with an extra step and an extra branch.
`ChannelSelector` is now concrete and `Select` returns `Option<CommunicationChannel>`, where no
value means no preferred channel is consented; nothing anywhere calls that state "not
contactable" any more. Scopes: `IConsentGate.cs`, `ConsentGate.cs` and `ConsentDecision.cs` are
deleted; `LeasingMessageAgent` step 1; `CliRunner`'s composition root; `ConsentGateTests`.
`consent_verified` is now earned by a record reaching step 1 at all, whichever way the selector
answered and whatever the record's `channel_preferences` list holds: the consent-driven
selection is the step that owns the state, and no input makes the verdict anything else (A14).
Evidence: the two expressions above, read side by side; `ChannelSelectorTests` green, with the
three `ConsentGateTests` cases that duplicated it dropped and the one case it did not prove
merged in.
Assumption: A14.

**D58. Every interface without a second implementation deleted (2026-09-09).** Question: which
interfaces stay. Options: keep the per-component interfaces, on the argument that a caller might
one day substitute one; or keep only the seams that have a real and an offline implementation
today. Recommendation: the second, which is S3's default and D7's commitment, both taken before
this sprint. `IChannelSelector`, `ISendScheduler`, `INextActionPlanner`, `IMessageAgent`,
`IEvaluator` and `IRecordWriter` each had exactly one implementation and no test substitute, so
every call through them was a hop to the only class that could answer, and Appendix B's rehearsal
rule counts each such hop as a finding. Exactly three interfaces remain in `src/Agent`:
`IMessageComposer`, `ICompletionClient` and `ISafetyValidator`, and each has a real
implementation in `src/Agent` and an offline one, the template composer for the first and a test
substitute for the other two. Scopes: the six interface files are deleted and their callers name
the concrete type; DESIGN.md section 5's seam column; README.md's architecture paragraph.
Evidence: S3, which records that eleven interfaces existed at the retrospective and eight had one
implementation and no test substitute, and D7, which commits to removing them as the sprint that
touches each one lands. Assumption: none.

**D59. The diagram is renumbered to the code, not the code reordered to the diagram
(2026-09-09).** Question: DESIGN.md section 5's flow numbered the orchestrator's steps compose,
validate, schedule, plan, and the orchestrator executes them plan, compose, schedule, validate.
One of the two had to move. Options: reorder the orchestrator to match the published diagram; or
renumber the diagram to match the executed order. Recommendation: the second, because the
executed order is forced and the diagram's is not. Two constraints fix it: the
composition-failure path returns the planner's `next_action`, so the plan must exist before
compose runs; and the orchestrator's own step 5 gate validates the final message, which carries
`send_at`, so scheduling must happen before validation. Reordering the code to the diagram would
break both. No code was reordered and no behavior changed; the six numbered steps now read 1
select the contactable channel, 2 plan the next action from the horizon, 3 compose, 4 schedule,
5 validate, 6 emit, in the order the file executes them. Scopes: DESIGN.md section 5's mermaid
diagram and README.md's copy of it. Evidence: `LeasingMessageAgent.RunUnguardedAsync` read top to
bottom, where the `ComposeOutcome.Failed` arm returns `Suppressed(..., nextAction, actionPlan)`
with a `nextAction` the planner produced above it, and where `validator.Validate` is called on
`finalMessage`, which is `draft with { SendAt = scheduled.SendAt }`. Assumption: none.

**Run and debug fact, Sprint 8 (2026-09-09).** `tests/Agent.Tests/Evaluation/BaselineNumbersTests.cs`
could not stay byte-for-byte unchanged through D58, which is the first time that has happened.
Line 34 declared the deleted `IMessageAgent`, so one token changed, `IMessageAgent` to
`LeasingMessageAgent`. Every pinned tally is untouched and all three theory cases still pass. The
suite moved from 553 tests to 551 for a separate reason, D57: three `ConsentGateTests` cases
duplicated `ChannelSelectorTests` and were dropped, and one case `ChannelSelectorTests` did not
prove was merged into it.

## Sprint 9 decisions, the Phase 6 remainder and fault injection (2026-09-09)

Playbook steps 80 and 81, then Phase 7 step 84. Numbered from D60 because D59 is the last
number Sprint 8 took. Nothing below is implemented: these are the decisions that scope the
sprint, written before any task (Pillar 3, DBT).

**D60. Phase 6's check is the playbook's, not this log's (2026-09-09).** Question: the
"Current phase" block at the top of this file states Phase 6's check as "the narration is
delivered without notes and the orchestrator reads as its steps in order";
`~/.agent-rules/PROJECT_PLAYBOOK.md` states it as "the documented one-line command produces
the output file, the diagnostics file, and the scorecard, and exits with the documented
code". Two checks on one gate is one open question, not one rule, and the log has been
holding the phase open against the check that is not the playbook's. Options: keep this
log's check and treat the playbook's as wording it grew out of; or adopt the playbook's and
put the narration back where the playbook puts it. Recommendation: the second. Evidence: the
playbook's Phase 6 is titled "Orchestration, entry points, and operations" and holds steps 72
to 81, none of which is a narration; delivering the narration aloud without notes is step 98,
inside Phase 8, "Documentation and review". This log and DESIGN.md section 9 both retitle
Phase 6 "Structure and narration", both state the fused check, and Sprint 8 was named for that
title while implementing steps 72 to 79. So the divergence is a phase renamed after the half
of step 72 it implemented, with step 98's rehearsal pulled forward onto its gate. What
changes: Phase 6 passes on the playbook's check once steps 80 and 81 land, and stops waiting
on a rehearsal the playbook does not gate it on; the narration rehearsal moves to Phase 8 step
98, where it is still owed. What does not change: `docs/NARRATION.md` stands as written, and
AGENTS.md's workflow rule that one record is narrated aloud before a PR merges is a project
rule that was never the phase gate and is untouched. No file under `src/` or `tests/` is
affected. Scopes: the Current phase block, replaced at the end of Sprint 9 and not before;
DESIGN.md section 9's phase table row for Phase 6; the order of Sprint 9, which is steps 80
and 81 and then step 84. Assumption: none.

**D61. What latency in the diagnostics means (2026-09-09).** Question: steps 75 and 80 name
latency per unit of work and per batch in the diagnostics record, and
`src/Agent/Orchestration/TaskDiagnostics.cs` is `(TaskId, Diagnostics, IngestNotes)` with no
latency member; latency exists only on the scorecard. What the number is, what it is measured
around, and how it relates to the scorecard's p95. Options: (a) leave latency on the scorecard
alone and read steps 75 and 80 as already satisfied by `--eval-report`; (b) start a second
stopwatch inside the agent and put its number on the diagnostics row; (c) put the number
`CliRunner` already measures on the diagnostics row, one measurement with two readers.
Recommendation: (c). Per record, `latency_ms` is the wall-clock elapsed of exactly one
`LeasingMessageAgent.RunAsync` call: the `Stopwatch` started at `CliRunner.cs:229` and stopped
at `:245`. Stated rather than implied, it excludes reading and parsing the input line,
`IngestNotes.Describe`, every output write, and the evaluator; it includes channel selection,
the plan, every compose attempt with any model call and its retry inside it, the schedule, and
the final safety gate. It is the same `double` already handed to `ScoredRun.LatencyMs` at
`:260`, passed to both from one variable, so the diagnostics file and the eval report can
never state two different latencies for one record. A record whose `RunAsync` threw gets no
diagnostics row at all and keeps getting none, so the member is non-nullable. Per batch: one
wall-clock elapsed around the record loop, and it does not go in the diagnostics file. That
file is a single JSON array of one row per unit of work, and a batch number in it needs either
an envelope around the array, which breaks every reader and every test built on the array
shape, or a synthetic row that is not a unit of work; it goes instead on the two artifacts
that are already per batch, the `Batch complete` log line at `CliRunner.cs:262` and the
scorecard, beside the p95 it already prints. Relation to the p95: the p95 is computed from
these same per-record numbers in `Scorecard.ComputeLatencyP95Ms`, so the diagnostics member is
its input and not a second opinion. It is wall clock and it moves: DESIGN.md section 9 records
18 ms in Sprint 6 and 22, 20 and 19 ms in Sprint 8 on unchanged code, and README records 19 to
23 ms across repeated runs of the same commands. So no test pins it.
`BaselineNumbersTests` scores with `LatencyMs: null` (line 41) and keeps doing so, and any
golden or round-trip test over a diagnostics row compares with the latency member excluded or
replaced by a fixed value, never with a number a run produced; a test asserting a latency
under a bound fails on a slow machine and proves nothing about this code. Scopes:
`TaskDiagnostics`, `CliRunner`'s record loop and its batch log line, `ScorecardFormatter`, the
`--diagnostics` row of OPERATIONS.md section 1, and README's latency paragraph. Evidence:
playbook steps 75 and 80; `CliRunner.cs:229`, `:245`, `:260`, `:262`; `Scorecard.cs` lines 32
to 37; DESIGN.md section 9's Sprint 8 paragraph; README's Latency paragraph. Assumption: A15.

**D62. What cost in the diagnostics means (2026-09-09).** Question: steps 75 and 80 name cost
per unit of work, and cost exists today only as dated prose in DESIGN.md section 9 and README.
What a cost member says on a record that made no model call, on one whose call was abandoned at
its timeout, and on one whose call completed; and whether the number is measured or derived
from a published price. Options: (a) a money member computed in code from a per-model price
constant; (b) a measured token member and no money anywhere in `src/`; (c) nothing, leaving
cost as prose. Recommendation: (b). Money is not computed in code, because a price constant is
a number no test in this suite can check: the check would be "is this still the vendor's list
price", which is a fact about a web page and not about this program, so the constant would go
stale silently and be believed. The dollar figure stays where it already is, dated and read
off the vendor's own usage page rather than estimated: DESIGN.md section 9's cost paragraph
($0.004 for the 2026-09-08 run, about $0.00017 per record) and README, each naming the model,
the date, and the published per-million rates it was priced at. A reader who needs today's
money multiplies today's price by the tokens the diagnostics measured. What is measured is the
token counts the vendor returns: `OpenAI.Chat.ChatCompletion.Usage` is an
`OpenAI.Chat.ChatTokenUsage` carrying `int InputTokenCount`, `int OutputTokenCount` and `int
TotalTokenCount`, confirmed on 2026-09-09 by reflection over the restored OpenAI 2.13.0
assembly rather than from documentation (playbook step 50, SCS). `OpenAiCompletionClient` does
not read it today, so the counts travel out on `ModelCompletion` beside `NetworkRetries` and
reach the diagnostics on `CompositionNotes` the way `NetworkRetries` already does, as a small
record of `Calls`, `CompletedCalls`, `InputTokens` and `OutputTokens`, from which abandoned
calls are `Calls` minus `CompletedCalls`. The three cases are three different facts and must
not collapse into one zero. No call made: the template composer issues no request, so the
member is null, which is the rule `CompositionNotes.NetworkRetries` already states for a
composer that makes no network call at all; null here means no model path ran, and the money
is a known zero stated once in README, not an unknown. Call abandoned at its timeout:
`OpenAiCompletionClient.CompleteAsync` throws its `TimeoutException` before it ever reads
`result.Value`, so there is no `Usage` to read, and the client cannot measure what an
abandoned call cost, while DESIGN.md section 9 records that the vendor billed roughly a third
of them in full, output tokens included. The member is therefore present with `Calls` counted
and `CompletedCalls` and both token counts zero, and the counted call is what stops zero
tokens from reading as free. Every record of the 2026-09-08 live run would read exactly that
way: calls made, no tokens returned, and a real bill. Call completed: the member carries that
call's input and output token counts, summed across the compose-validate loop's attempts the
way `NetworkRetries` is already summed
(`ValidatingMessageComposerTests.ComposeAsync_FirstAttemptHasRetriesThenFailsValidation_SecondAttemptSucceeds_SumsNetworkRetries`).
Per batch, D61's rule holds unchanged: the diagnostics file stays one row per unit of work and
the batch totals go on the scorecard and the `Batch complete` log line. Scopes:
`ModelCompletion`, `OpenAiCompletionClient.CompleteAsync`, `CompositionNotes`,
`ValidatingMessageComposer`'s accumulation, the `--diagnostics` row of OPERATIONS.md section
1, README's money paragraph. Out of scope, explicitly: any dollar arithmetic under `src/`.
Evidence: playbook steps 75 and 80; the reflection probe above; `OpenAiCompletionClient.cs`
lines 79 to 87, where the timeout is thrown before `result.Value` is read; `CompositionNotes.cs`'s
`NetworkRetries` rule; DESIGN.md section 9's cost paragraph. Assumption: A18.

**D63. Step 81 is the Phase 6 check run, not a second scoring pass (2026-09-09).** Question:
step 81 says run the examples end to end from the documented command and check the output by
hand against the expected records, and no result for it is recorded anywhere in `docs/`,
searched 2026-09-09. What a by-hand check is here, what artifact records it, and what it adds
over the evaluator, which is automated and already runs. Options: (a) a per-field manual
comparison of every output row against its `expected`, written up; (b) name that comparison as
the one the evaluator already makes, scope it out with the reason, and keep only what a person
checks that the scorer cannot; (c) leave step 81 unrecorded. Recommendation: (b). Per field,
per record, against the label is exactly what `Evaluator` does: D13 d fixes the label as the
oracle and forbids scoring against the product's own tables, DESIGN.md section 6 names the
checks, and `--eval-report` prints one verdict per check per record plus a per-check tally. A
person redoing that across 26 rows would be running the same comparison less reliably, and any
disagreement would mean the scorer is wrong, which is what `ScorerProofTests` exists to rule
out. So the per-field re-comparison is a deliberate scope-out and is recorded in
docs/CODE_REVIEW.md with that reason rather than performed as ceremony. What is kept is the
half of step 81 the scorer cannot do, and it is Phase 6's check itself: run each documented
one-line command from the repo root, confirm the output file, the diagnostics file, the review
queue and the scorecard all appear, and confirm the exit code is the documented one, 0 on
`sample.jsonl`, 0 on `holdout_12.jsonl` and 2 on `synthetic_12.jsonl` for its malformed line.
Two things a person adds there that no automated check makes today. First, that the
scorecard's tally lines agree with its own rows: `Scorecard` computes its tallies and its p95
once, in field initializers, so a `with` copy that replaces `RecordScores` prints a report
whose rows and totals disagree, a failure mode this repo has already recorded and no test
watches for across the CLI's own printed output. Second, that one record read end to end, its
input line, its output row, its diagnostics row and its scorecard row, tells one consistent
story. The artifact: a dated subsection of DESIGN.md section 9, where every other run record
in this project lives, naming per set the exact command, the files that appeared, the exit
code, and the two confirmations above. Not a new file: the run record has one home and a
second one splits it. Scopes: DESIGN.md section 9 and its phase table row for Phase 6,
docs/CODE_REVIEW.md. Evidence: playbook step 81 and Phase 6's check; D13 d; `ScorerProofTests`;
AGENTS.md's `Scorecard` gotcha; the absence of any step 81 record in `docs/`. Assumption: A19,
which is why the by-hand check reports what the sets do and never moves a rule to make a row
match.

**D64. Fault injection: five faults are already proved, one is not (2026-09-09).** Question:
playbook step 84 names six faults, network down, rate limit, malformed response, unknown
locale, corrupt input line, and disk full on output, and asks for graceful degradation and
correct diagnostics for each. Which are already proved, which Sprint 9 proves, which are
scoped out, and what file carries the results Phase 7's check asks for. Options: write a new
fault-injection test class covering all six; or audit the suite by test name first and add
only what is not proved. Recommendation: the second, on the audit below, run 2026-09-09.
Network down is proved: `OpenAiMessageComposerTests.ComposeAsync_CompletionClientThrowsHttpRequestException_ReturnsFailureNotException`
turns a transport failure into a `ComposeOutcome.Failed` naming the exception category and not
its text, and `ValidatingMessageComposerTests.ComposeAsync_ComposerKeepsFailing_FallsBackToSafeComposer`
plus `ComposeAsync_BothAttemptsBad_ReportsTheFallbackComposerAndEveryAttempt` prove the
degradation and the diagnostics, `composition.composer: template` with the attempts counted on
a run that asked for `openai`; the whole-set run with outbound HTTPS blocked at the process
level is the Phase 4 check and is recorded in DESIGN.md section 9. Rate limit is proved:
`OpenAiCompletionClientTests.CompleteAsync_TransientFailureThenSuccess_RetriesOnceAndReportsIt`
injects a 429 then a 200 and asserts one retry reported and two HTTP calls, and
`CompleteAsync_TransientFailureEveryTime_StopsAfterTheBoundedRetry` proves the retry is
bounded at two attempts, on a 503 rather than a 429; the SDK retry policy is status-agnostic
across the transient set it knows, so the exhaustion path is proved once and not per status.
Malformed response is proved:
`OpenAiMessageComposerTests.ComposeAsync_MalformedJson_ReturnsFailureNotException`,
`ComposeAsync_MissingRequiredFields_ReturnsFailure`, `ComposeAsync_NullJsonBody_ReturnsFailure`
and `ComposeAsync_ModelReturnsWrongCtaType_ReturnsFailure` at the composer, and
`OpenAiCompletionClientTests.CompleteAsync_ResponseHasNoContent_ThrowsInvalidOperationException`
and `CompleteAsync_ResponseHasNoChoice_ThrowsInvalidOperationException` at the client, where a
200 carrying no choice is named rather than escaping as an `ArgumentOutOfRangeException`.
Unknown locale is proved in all three of the forms this product has: an unserved language tag
by `TemplateMessageComposerTests.ComposeAsync_LanguageWithNoTemplateSet_ComposesInEnglishAndReportsTheLocaleNotApplied`
(A13, `locale_applied: false`); an unrecognized timezone id by
`SendSchedulerTests.Resolve_UnknownTimeZoneId_ResolvesInUtc`,
`TimeZonesTests.ResolveOrUtc_UnknownId_ReturnsUtc`, `TimeZonesTests.ToLocalDate_UnknownZone_UsesTheUtcDate`
and, for the diagnostics half, `IngestNotesTests.Describe_UnrecognizedTimezone_NamesItAsDefaultedWithoutTheRecordsOwnValue`
(A6); and an unrecognized channel name by
`JsonlRecordReaderTests.ReadAll_UnrecognizedChannelName_ParsesAsUnknownChannel`,
`ReadAll_ChannelPreferenceEntriesNotChannelNames_ParseAsUnknown` and
`ReadAll_ChannelPreferenceNumericStringMatchingARealOrdinal_ParsesAsUnknown` (A3). Corrupt
input line is proved end to end:
`JsonlRecordReaderTests.ReadAll_ReturnsFailureWithLineNumber_WhenLineIsMalformedJson`,
`...WhenLineDeserializesToNull`, `...WhenLineIsValidJsonButNotAnObject`,
`ReadAll_OneBadLineAmongGoodOnes_ReturnsEveryOtherRecord`,
`ReadAll_BlankLinesBeforeFailingLine_CountTowardTheLineNumber`,
`ReadAll_MalformedUndeclaredMember_FailureNamesTheLineAndNoTextTheRecordWrote` and
`ReadAll_ParsesSyntheticTwelve_TwelveSuccessRowsAndOneFailureNamingLineEleven` at the reader,
and `CliRunnerTests.RunAsync_OneLineFailsToParse_OtherRecordStillWrittenAndReturnsPartialFailure`
and `RunAsync_ReplayWithAnUnparsableInputLine_ReturnsPartialFailure` at the entry point, where
the documented `synthetic_12.jsonl` run exits 2 by design. Disk full on output is the one
fault that is not proved, and the audit found a real gap behind it rather than a missing test:
`--log-file` fails fast on `IOException` or `UnauthorizedAccessException` with a clean stderr
line and exit code 1 (`CliRunner.cs:105`, proved by
`CliRunnerTests.RunAsync_LogFilePathHasNoParentDirectory_WritesCleanErrorAndReturnsUsageError`),
while `--output`, `--diagnostics` and `--review-queue` open unguarded at `CliRunner.cs:195` to
`:197` and `--eval-report` writes unguarded at `:381`, and `Program.cs` installs no handler, so
a full or unwritable volume ends the process on an unhandled exception with a stack trace on
stderr and an exit code that is none of 0, 1 or 2. That contradicts step 79's documented codes
and step 77's fail fast on bad paths before doing work that costs time or money. In scope for
Sprint 9: give the three output streams the guard `--log-file` already has, opened before the
record loop so a bad path costs nothing, and give the scorecard write the same guard where it
is. Scoped out: producing a genuinely full volume. The portable and testable form of disk full
is the open that fails, which the guard answers identically for every cause of a failed open, a
volume that is already full included. Narrowed on 2026-09-09 from "the write that fails, which
the guard answers identically for every cause", which claimed more than the guard does: all
three batch guards sit at the open and the stream stays open across the record loop, so a volume
that fills mid-batch throws from inside the writer, downstream of every guard in `CliRunner`,
and that run still ends on an unhandled exception. `--eval-report` is the exception on both
counts, being one guarded `File.WriteAllTextAsync` rather than a stream held open, so its own
write is covered. An actual out-of-space condition, at the open or after it, needs a virtual
disk or a filesystem quota, a machine setup this suite cannot carry and CI cannot reproduce.
That scope-out is recorded in docs/CODE_REVIEW.md and in fault 6 of docs/FAULT_INJECTION.md in
the narrowed form, which is the form this paragraph now states. The results file Phase 7's check asks for is `docs/FAULT_INJECTION.md`, one row
per fault naming the injection, what the product did, what the diagnostics said, the exit
code, and the test that pins it. Not at the repo root: `.gitignore` ignores `/out*.json`,
`/diag*.json`, `/q_*.json`, `/eval*.txt` and `/*.log` there, so an artifact a run writes at the
root is not a file in the repo, which is what the check asks for. Not DESIGN.md section 9
either: section 9 records what a run measured, and this records what a fault did, six rows
with no tallies. Scopes: Sprint 9's test work, `CliRunner`'s output path handling,
`docs/FAULT_INJECTION.md`, `docs/CODE_REVIEW.md`, and the exit-code table in OPERATIONS.md
section 2. Evidence: the audit above by test name; `CliRunner.cs:105` against `:195` to `:197`
and `:381`; `Program.cs`; `.gitignore`; playbook steps 77, 79 and 84 and Phase 7's check.
Assumption: none.

**D65. The input paths get the guard the output paths got (2026-09-09).** Question: D64 gave
`--output`, `--diagnostics` and `--review-queue` the guard `--log-file` already had, and gave
`--eval-report` the same guard at its write, so an unwritable path is one stderr line naming the
flag and exit code 1. `--input` and `--replay` were left out, because D64's subject is the
disk-full fault and it names only the paths a run writes. Both still open unguarded: `ReadInput`
builds `new StreamReader(inputPath)` at `CliRunner.cs:396`, reached from `RunAsync` at `:140`
and again from `ReplayAsync` at `:362`, and `ReplayAsync` builds `new StreamReader(replayPath)`
at `:365`; `Program.cs` installs no handler. Measured on 2026-09-09 against the built CLI rather
than reasoned about: `--input` naming a file that does not exist ends the process with
`Unhandled exception. System.IO.FileNotFoundException`, a nine-frame stack trace on stderr whose
top frame is `CliRunner.cs:line 396`, and exit code -532462766 (0xE0434352, the CLR's
unhandled-exception code), which is none of 0, 1 or 2; `--replay` naming a file that does not
exist does the same from `:365`. So the question is whether the two reader paths get the same
guard, and what exit code an input file that will not open deserves, given that the documented
codes are 0 success, 1 usage error and 2 partial failure.

Options: (a) leave them unguarded and treat a bad input path as an operator error the operating
system already reports; (b) guard both with `OpenOutputStream`'s mirror and return 1 for every
input path that will not open; (c) guard both but split the code, 1 for a path the user typed
wrongly and 2 for a file that exists and cannot be read, on the ground that the second is a
failure of the run rather than of the command line. Recommendation: (b). Exit 2 is a per-record
fact everywhere else it is used: `JsonlRecordReader.ReadAll` returns one failure row per bad
line and `ReadInput` counts them, so 2 means some records were processed and some were not. A
file that never opened has no lines and no records, so 2 would report a partial success that did
not happen, and anything scripting on the code would retry the good half of a batch that has no
good half. (c) also asks the exception type to draw a line it does not draw:
`FileNotFoundException` and `DirectoryNotFoundException` both derive from `IOException`, and a
locked file, a full volume, a bad drive letter and a denied ACL all arrive as `IOException` or
`UnauthorizedAccessException` without saying which of the two stories they are. The four output
flags already answer 1 for that whole class, a locked or full volume included, so a reader path
answering 2 for the same operating-system fact would make one program say two things about one
kind of failure.

The shape, stated so it is not chosen again while building: `private static Result<StreamReader>
OpenInputReader(string flag, string path)`, the mirror of `OpenOutputStream` at `:472`, with the
same message text (`Could not open {flag} '{path}': {ex.ToDiagnosticString()}`), the same filter
(`IOException or UnauthorizedAccessException`, unchanged from D64: a wider one here and a
narrower one there is the thing to avoid), reported through `ReportFailure` so the log line and
the stderr line keep one wording. `ReadInput` takes the opened reader instead of the path and
keeps its tuple return; its two callers own the `using` and the `return CliExitCodes.UsageError`.
Nothing moves in the order: the input open stays where `ReadInput` is called today, which is
already before the composer is built, so a bad path still costs no time and no money (step 77).
`--replay` is guarded at `:365`, in place.

One hole found while measuring this, closed here rather than left for the next reader to find: an
option given an empty value ends the process the same way, and every guard D64 wrote has the same
hole, because an empty path throws `ArgumentException` and not `IOException`. Measured, all four
on 2026-09-09: `--input ""` throws `System.ArgumentException: The value cannot be an empty string.
(Parameter 'path')` at `:396`, `--output ""` throws it at `:481` inside `OpenOutputStream`'s try,
`--log-file ""` at `:103`, and `--eval-report ""` at `:446`. It is not closed by widening a catch
filter to `ArgumentException`, which would ask three guards to swallow an exception a bug in the
same try block could also throw; it is closed in the argument parsing, before the existing usage
check at `:64`, by the rule that no argument of this program may be empty. Every flag either
takes a value or is a presence flag, and no value any of them takes has a meaning when empty:
`--composer ""` is already an invalid composer name and `--now ""` an unparsable date-time, both
exit 1 today. So one scan of `args` for a zero-length entry, reporting the flag that precedes it
when there is one and the position when there is not, is one check with no list of flags to keep
in sync. Exit code 1, one stderr line, no stack trace.

Tests, named after `RunAsync_LogFilePathHasNoParentDirectory_WritesCleanErrorAndReturnsUsageError`,
which is the case this copies: `RunAsync_InputPathDoesNotExist_WritesCleanErrorAndReturnsUsageError`,
`RunAsync_ReplayPathDoesNotExist_WritesCleanErrorAndReturnsUsageError`,
`RunAsync_OptionGivenAnEmptyValue_WritesCleanErrorAndReturnsUsageError` and
`RunAsync_FirstArgumentIsEmpty_WritesCleanErrorAndReturnsUsageError`, each written failing against
the current code first and each asserting the exit code, the one stderr line, and the absence of a
stack trace in it. Scopes: `CliRunner`'s argument parsing and both reader opens,
`Agent.Cli.Tests/CliRunnerTests.cs`, the exit-code table in OPERATIONS.md section 2, and the
by-hand paragraph of DESIGN.md section 9 that already records the four output flags checked on
2026-09-08, which gains the reader paths beside them. Not `docs/FAULT_INJECTION.md`: step 84 names
six faults and an unreadable input path is not one of them, so that file stays six rows (D64).
Evidence: the four measured failures above; `CliRunner.cs:396`, `:365`, `:362`, `:140`, `:481`,
`:472`, `:446`, `:103`, `:64`; `Program.cs`; playbook steps 77 and 79; D64. Assumption: none.

**D65 addendum, the six failures measured after (2026-09-09).** D65 recorded what each case did
before the guard. Measured against the built CLI after it, from the repo root: `--input` naming a
file that does not exist writes `Could not open --input '<path>': FileNotFoundException: Could
not find file '<absolute path>'.` and exits 1, with no stack frame anywhere in its output, where
before it ended the process on an unhandled `FileNotFoundException` with a nine-frame stack trace
and exit -532462766; `--replay` naming a file that does not exist does the same, naming
`--replay`. Each of `--input ""`, `--output ""`, `--log-file ""` and `--eval-report ""` writes
`The argument after '<flag>' is empty: no argument of this program may be empty.` and exits 1,
where before each ended the process on an unhandled `ArgumentException: The value cannot be an
empty string. (Parameter 'path')`. An empty first argument, which follows no flag, writes
`Argument 1 is empty: no argument of this program may be empty.` and also exits 1; those are the
only two forms the scan emits. One difference between the two halves, measured rather than
assumed and unchanged from D64: the two reader guards print the failure twice and the empty-value
cases print it once. `ReportFailure` writes one failure through the logger and through the error
writer both, and the console sink is the CLI's own `error` stream (OPERATIONS.md section 3), so a
guarded open produces a timestamped `[Error] Agent.Cli.CliRunner:` line and the plain stderr line
under it, exactly as D64's four output guards do. The empty-value scan runs before any logger
exists, at `CliRunner.cs:72` to `:83`, and writes to `error` directly, so it has one line to
print. Neither form carries a stack frame, which is what the tests assert. Evidence: the runs
above; `CliRunner.cs:72` to `:83`, `:164`, `:399`, `:413`, `:512` to `:516`, `:548`; D64; D65's
own before measurements, which are not re-measured here because that would mean reverting `src/`.

**D66. The model cost vanishes on exactly the records that failed (2026-09-09).** Question: D62
put the token counts on `CompositionNotes.ModelCost`, and `CompositionNotes` is the one
diagnostics member that is absent on every record with no message. `LeasingMessageAgent` nulls it
at `:127` for a refused draft and at `:197` for a safety-suppressed one, and `Suppressed` never
sets it, at `:75` for no consented channel and at `:133` for a composition failure. The loss
starts a layer lower than that: `ValidatingMessageComposer` accumulates `discardedModelCost`
across every attempt (`:72` for a draft it rejected, `:91` for an attempt that produced none) and
then drops the accumulation at `:103`, where the fallback produced no message and the `Failed` it
builds takes the default `ModelCost: null`, and at `:112`, where `ComposeOutcome.Refused` cannot
carry one at all, being defined with `ModelCost: null` at `ComposeOutcome.cs:49` on the stated
ground that a refused record carries no composition notes and so has nowhere to report it. A
record that made two model calls and ended with no message therefore reports no model call, and
`CliRunner.cs:292` sums the batch total off `result.Diagnostics.Composition?.ModelCost`, so the
batch total loses the same tokens. `NetworkRetries` is dropped at the same two lines by the same
mechanism. The question is what carries the cost of a record whose run produced no message.

The evidence, stated exactly, because the number in a file today is not yet wrong. No documented
run understates: on all three sets `composition.model_cost` is null on all 26 rows, the review
queue is empty, and every suppression is `no_contact_consent`, which is step 1 returning before
the composer runs, so there is no cost to lose (DESIGN.md section 9, step 71's numbers). The
2026-09-08 live run does not understate either: every call was abandoned at its timeout, the
template fallback answered every record, and `WithAttempts` at `:108` carries a discarded
attempt's cost into the winner, so the abandoned calls survive; that run also predates the field.
What loses is a record that ends with no message on a run that called the model, and the
constructed steering record of D48, whose own `city_interest` reads `families only`, is that
record: under `--composer openai` its two abandoned calls accumulate into `discardedModelCost`,
the template fallback reproduces the steering language from the record's own data, `:112` refuses,
and the row reports no model call and no retries while the vendor billed for roughly a third of
what was asked (D31 addendum). So the understatement is structural and preventive rather than a
figure to correct, and it lands on the failing record every time.

Options: (a) move the two numbers off `CompositionNotes` onto `AgentDiagnostics`, so they survive
a nulled composition; (b) keep the carrier and stop nulling it; (c) keep the carrier, accept the
understatement, and document it as a known limit of the field. (a) costs two members on
`AgentDiagnostics`, two on the `ComposeOutcome` base, and moving two keys out of the `composition`
object to the diagnostics row's own level in every golden over one
(`AgentDiagnosticsTests.cs:177`, `:193`, `:215`, `CliRunnerTests.cs:1490`, `:1529`), plus every
prose citation of the two keys by their path: the `--diagnostics` row of OPERATIONS.md section 1
and the `composition` walkthrough of its section 2, DESIGN.md's Sprint 9 cost paragraph and its
live-run paragraph, README's money paragraph, and three rows of `docs/FAULT_INJECTION.md`. It
re-opens
nothing: D62's null-is-not-zero rule is unchanged, and so is D24's rule for what the notes are. A
reader of a diagnostics file would see a suppressed row with no `composition` object and
`model_cost: {calls: 2, completed_calls: 0, input_tokens: 0, output_tokens: 0}` beside its
`latency_ms`. (b) costs more than it looks: `Composer` is a non-nullable string and `LocaleApplied`
a non-nullable bool, so a record with no message must be handed a composer name and a locale
verdict for a message that does not exist, or those three members become nullable and every reader
learns a fourth state. It re-opens D3's addendum, which nulled `Composition` on the final-safety
branch precisely so that a record with nothing on the wire cannot name a composer as if its draft
had shipped, and with it D24, D48's rule that a refused draft carries no notes, and
`AgentDiagnostics`'s own stated contract. A reader would see `composition.composer: template` on a
record that sent nothing, which is the exact misreading D3's addendum was written to remove. (c)
costs nothing to build, and a reader would see a `safety_violation` row with no `composition`
object, from which the only available reading is that no model call was made, on a record that
made two. That is word for word the misreading D62 named and fixed for the template-fallback case
("the record would fall back to the template composer and read as a run that never called a model,
while the vendor billed for the abandoned attempts anyway"), so (c) fixes that reading where the
run succeeded and leaves it where the money actually went.

Recommendation: (a), and `NetworkRetries` moves with `ModelCost` in the same change. It is the
same class of fact, what this record's run spent, which is what `latency_ms` is and why D61 put
that on `TaskDiagnostics` where every row carries it whatever the outcome; it is dropped at the
same two lines by the same mechanism; and D28's own visibility goal, that a retry a discarded
attempt spent is still a retry this record spent, fails there exactly as the token count does.
Splitting the pair would leave two adjacent numbers on two carriers and make a reader know which
of them survives a suppression. What stays on `CompositionNotes` is `Composer`, `Attempts` and
`LocaleApplied`, which are properties of a returned message and not of the record: which
implementation wrote it, how many calls it took to get it, whether its language was served. A
record with no message has no answer to those three, so `Composition` stays null there exactly as
D24, D3's addendum and D48 left it, and that is the answer to the question (a) raises about what
else on the notes belongs to the record.

The shape, so nothing below is decided again while building. `ComposeOutcome` gains
`ModelCostNotes? ModelCost` and `int? NetworkRetries` as init-only properties on the base type, so
D48's private constructor and closed three-case hierarchy stay as they are and all three cases
answer for both; `NoMessage.ModelCost` and `Failed`'s positional `ModelCost` go away in favour of
them, and `Refused`'s comment about having no cost of its own is rewritten, because the reason it
gives stops being true. `ValidatingMessageComposer` stays the one place that stamps the totals,
the way it already stamps `Attempts` (D24): `WithAttempts` sets both on the `Composed` it returns,
and `:103` and `:112` set them from `discardedModelCost` and `discardedNetworkRetries` instead of
dropping them. Not closed here, and still stated: an attempt that built no `ComposedMessage` has
no capturable retry count, because that count rides on `ModelCompletion` and an abandoned call
returns none, which is the gap D28's addendum already names. `CompositionNotes` becomes
`(Composer, Attempts, LocaleApplied)` and `ForComposer` loses its two optional parameters.
`AgentDiagnostics` gains `ModelCostNotes? ModelCost = null` and `int? NetworkRetries = null` as its
last two positional members, so the existing key order of a diagnostics row is untouched and the
two keys are appended to it. `LeasingMessageAgent` reads both off `composeOutcome` once, at `:109`
before the switch, and passes them into every `AgentDiagnostics` it builds, including `Suppressed`,
which gains the two parameters: null and null at `:75`, where the composer never ran, and the
outcome's own at `:133`. `CliRunner.cs:292` sums `result.Diagnostics.ModelCost`.

The test that proves it, written failing against the current code first: the steering record driven
through `LeasingMessageAgent` with a composer that abandons both attempts and a fallback that
reproduces the violation, asserting the suppressed record's diagnostics carry two calls, zero
completed and zero tokens, where today they carry no cost at all; and a `CliRunner` case asserting
the batch total counts that record. Scopes: `ComposeOutcome`, `CompositionNotes`,
`AgentDiagnostics`, `ValidatingMessageComposer`, `LeasingMessageAgent`, `CliRunner`'s batch sum,
the five goldens named above, the `--diagnostics` row of OPERATIONS.md section 1 and the
`network_retries` sentence of its section 2, DESIGN.md's cost paragraphs, README's money
paragraph, and `docs/FAULT_INJECTION.md`'s citations of the two keys. Out of scope, unchanged
from D62: any
dollar arithmetic under `src/`. Evidence: the line numbers above, all read on 2026-09-09;
DESIGN.md section 9's step 71 numbers and its cost paragraph; D31's addendum for the third of
abandoned calls billed in full; D28's addendum for the retry gap this does not close; D62, which
this corrects. Assumption: A18, D62's own.

**D66 addendum, what building it changed (2026-09-09).** One member of the shape above came out
differently and the paragraph is amended rather than left reading as though it had not.
`discardedNetworkRetries` in `ValidatingMessageComposer` is `int?` initialised to null, not an
`int` initialised to zero, and the attempts are folded together by a private `AddRetries` helper
that mirrors `ModelCostNotes.Add`: null plus anything is that thing. An `int` starting at zero
cannot distinguish "no call was made" from "calls were made and measured zero retries", and a
record whose every attempt stayed offline would have reported a measured zero for calls that
never happened, which is the null-is-not-zero rule D62 and D28 both state and this decision was
written to preserve, not to break. `ModelCost` needed no equivalent because `ModelCostNotes?` was
already nullable and `Add` already stated the rule. Everything else landed as the shape says:
`ComposeOutcome` carries both as init-only properties on the base type, `CompositionNotes` is
`(Composer, Attempts, LocaleApplied)` with `ForComposer(composer, localeApplied)`,
`AgentDiagnostics` carries `ModelCost` and `NetworkRetries` as its last two positional members so
the two keys are appended to the existing key order, and `Suppressed` takes null and null where
the composer never ran. Three line numbers in the paragraph above are pre-change and have moved
with the code: the batch sum is `CliRunner.cs:328`, and the two no-message exits that now stamp
the accumulations are `ValidatingMessageComposer.cs:109` for `Failed` and `:122` for `Refused`.
On the wire the two keys sit on the `diagnostics` object, beside `composition` and not beside
`latency_ms`, which is one level out; the paragraph above says "beside its `latency_ms`" and that
is loose. Evidence: `ValidatingMessageComposer.cs:47`, `:66`, `:74`, `:109`, `:122`, `:136` and
`:154` to `:164`; `ModelCostNotes.cs:27`; `CompositionNotes.cs`; `AgentDiagnostics.cs`;
`CliRunner.cs:328`; a diagnostics file written by the documented `sample.jsonl` run, all read
2026-09-09.

**D67. The fallback outcome's own spend is dropped at both no-message exits
(2026-09-09).** Question: D66 moved the two spend counts onto `ComposeOutcome` so the
compose-validate loop's two no-message exits could carry them, and both now do. What they carry
is the accumulation and only the accumulation. At `ValidatingMessageComposer.cs:109` the `Failed`
is built with `ModelCost = discardedModelCost` and `NetworkRetries = discardedNetworkRetries`,
and the `NoMessage` the fallback composer returned is read for its `Error` alone, so whatever
that outcome measured is dropped; at `:122` the `Refused` does the same to the `Composed` the
fallback returned. `WithAttempts` at `:136` is not symmetric with either: it adds the winning
attempt's own counts to the accumulation through `ModelCostNotes.Add` and `AddRetries`. So one
path in this class sums both sources and two paths sum one. Nothing in the shipped wiring
produces the difference: `CliRunner.cs:182` builds one `TemplateMessageComposer` and passes it as
the fallback at `:210`, and `TemplateMessageComposer.ComposeAsync` returns
`new ComposeOutcome.Composed(composed)` with neither property set, so both are null on every
fallback outcome this program can construct and adding null changes nothing. The `Failed` exit is
further unreachable today, because that composer never returns a `NoMessage` at all. Options,
none taken: (a) add the fallback outcome's own counts at both exits, which makes the three paths
read alike; (b) state on `ComposeOutcome` that a fallback composer is one that never spends, and
assert it; (c) leave it and record the asymmetry here. Recommendation: not made. Each option is a
decision about what a fallback composer is allowed to be, and DBT puts that decision before the
task rather than patching a line whose behavior no input can currently reach. Recorded so the
next reader of that method does not have to rediscover it, and so it cannot be flagged as an
unrecorded defect. Scopes: nothing yet; whichever option is taken scopes
`ValidatingMessageComposer`'s two no-message exits and their tests. Evidence:
`ValidatingMessageComposer.cs:104` to `:126` and `:136` to `:148`; `TemplateMessageComposer.cs:55`;
`CliRunner.cs:182` and `:207` to `:212`, all read 2026-09-09. Assumption: none. Taken
2026-09-10 on the requester's "dont defer do now", option (a): both no-message exits after the
fallback now add the fallback outcome's own counts to what the rejected attempts spent, the
`Failed` through `ModelCostNotes.Add(discardedModelCost, fallbackOutcome.ModelCost)` and
`AddRetries(discardedNetworkRetries, fallbackOutcome.NetworkRetries)`, the `Refused` the same with
the fallback's `Composed`, so all three exits sum both sources as `WithAttempts` does. This
decides what a fallback composer may be: one that spends is billed on every exit, not only on the
one that ships. No shipped output moves, because `TemplateMessageComposer`, the only fallback
`CliRunner` wires, sets neither count. Evidence: two new tests in
`ValidatingMessageComposerTests.cs` with a fallback that reports its own spend failed on the
unfixed code, reporting the attempts' sum (2, 2, 22, 14) where (3, 3, 27, 17) and (3, 2, 22, 14)
were expected, and pass after; `.\test.ps1` exit 0, 594 and 87 tests, 100 percent on both
modules. The line numbers cited above are the file as it stood on 2026-09-09.

**Run and debug fact, five review findings fixed (2026-09-10).** A Claude Code review of PR #26
and an Antigravity (Gemini 3.8 Flash) review of the same PR together found five real defects,
none of them D67 (which both reviews independently read and left alone, since it is already
recorded above as open). Each is fixed here with a failing test written first and confirmed to
fail against the unfixed code before the fix landed, per this repo's TDD rule; none is a new
decision, since none chooses between options DBT would gate.

The whitespace-only argument crash (Antigravity). The D65 empty-argument scan at
`CliRunner.cs:72` checked `args[index].Length != 0`, so `--input "   "` passed the scan, reached
`OpenInputReader`, and `Path.GetFullPath` threw `ArgumentException: The path is empty.` unhandled
- the exact D65 was meant to close, just on a blank string instead of an empty one. Confirmed by
running `RunAsync_OptionGivenAWhitespaceOnlyValue_WritesCleanErrorAndReturnsUsageError` before the
fix: it failed with that stack trace. Fixed by widening the condition to
`!string.IsNullOrWhiteSpace(args[index])`; the message text is unchanged, since a blank path is
the same operator-facing fact an empty one is.

NetworkRetries dropped on three of four `OpenAiMessageComposer` `Failed` exits, and on the
compose-validate loop's own `NoMessage` accumulation branch (Claude Code). Once a completion
succeeds, `completion.NetworkRetries` was in scope at the JSON-parse-failure, missing-fields, and
wrong-`cta_type` exits (`OpenAiMessageComposer.cs:136`, `:145`, `:166`) but only the `Composed`
exit (`:203`) read it; `ValidatingMessageComposer.cs`'s loop separately accumulated
`discardedModelCost` from a `NoMessage` attempt but never `discardedNetworkRetries` (`:94`). A
transport retry spent on an attempt that later failed downstream (a real, not abandoned, call)
read as `network_retries: null` instead of the count it actually spent. `SequenceMessageComposer`
(`tests/Agent.Tests/TestSupport/SequenceMessageComposer.cs`) had the identical gap on its own
`Failed` case, which is why no existing test could reach the scenario; fixed there first so the
production fix could be tested at all. Four new tests pin the fix:
`ComposeAsync_CompletedCallReturnedMalformedJson_FailureCarriesTheCompletionsRetries`,
`ComposeAsync_CompletedCallMissingRequiredFields_FailureCarriesTheCompletionsRetries` and
`ComposeAsync_CompletedCallReturnsWrongCtaType_FailureCarriesTheCompletionsRetries` in
`OpenAiMessageComposerTests.cs`, and
`ComposeAsync_FirstAttemptFailsWithRetriesThenSecondSucceeds_SumsNetworkRetriesFromTheFailedAttempt`
in `ValidatingMessageComposerTests.cs`; all four failed before the fix (0 instead of the expected
count) and pass after it.

A 200 with no completion choice discarded a real usage block (Claude Code). `result.Value` is
already read by the time `ChatCompletion.get_Content()` throws `ArgumentOutOfRangeException` on
an empty `Choices` list (`OpenAiCompletionClient.cs`), so `result.Value.Usage` was readable but
the old code threw a bare `InvalidOperationException` without reading it, and
`OpenAiMessageComposer`'s catch folded the case into the same zero-tokens bucket a genuinely
abandoned timeout gets, contrary to D62's own rule that the three cost states "must not collapse
into one zero." Fixed with a new type, `NoCompletionChoiceException` (still an
`InvalidOperationException`, so every catch that does not know about it keeps working), which
reads `result.Value.Usage` before throwing and carries the tokens on itself; `OpenAiMessageComposer`
gets a new catch clause ahead of its general one that turns them into a `ModelCostNotes` with
`CompletedCalls: 1` and the real counts. Making the exception type more specific broke
`CompleteAsync_ResponseHasNoChoice_ThrowsInvalidOperationException`'s exact-type assertion (xUnit's
`Assert.ThrowsAsync<T>` requires an exact match, not a subtype), so that test now asserts
`NoCompletionChoiceException` instead; two new tests,
`CompleteAsync_ResponseHasNoChoiceButCarriesUsage_ThrowsWithTheVendorsTokenCounts` and
`CompleteAsync_ResponseHasNoChoiceAndNoUsage_ThrowsWithZeroTokens`, pin the token-carrying and
zero-token cases, and
`ComposeAsync_CompletedCallHadNoChoiceButCarriedUsage_FailureCountsTheCompletedCallAndItsTokens`
pins the composer-level outcome.

Four independent file-open guard bodies in `CliRunner.cs`, and six near-identical
Result-unwrap-then-return blocks at their call sites (Claude Code, altitude and simplification).
The `--log-file` guard, `OpenOutputStream`, `OpenInputReader`, and the `--eval-report` write each
restated the same `catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)`
filter and the same message template independently, past this repo's own "extract on the third
occurrence" rule. Collapsed into three shared private methods - `TryOpen<T>` (a synchronous open
returning `Result<T>`), `TryOpenOptional<T>` (the same for a flag whose absence is success, not
failure), and `TryPerformAsync` (the write-shaped sibling, for `--eval-report`) - all built on one
filter and one message format, at `CliRunner.cs:514`, `:528` and `:544`. The six call sites that
unwrapped a `Result<T>` and returned `CliExitCodes.UsageError` on failure collapsed onto one
instance helper, `ReportIfFailed<T>` (`:561`; an instance method rather than static, because it
calls `this.ReportFailure`). Pure refactor, not a behavior change: no new test was needed or
added, and the existing suite (which already exercised both the success and failure path of every
guard) is what verifies it.

Evidence for all five: `dotnet build` clean, `.\test.ps1` exit code 0, 662 tests (584 in
`Agent.Tests`, up from 577; 78 in `Agent.Cli.Tests`, up from 77) all passing, 100 percent line,
branch and method coverage on both modules.

## Sprint 10 decisions, the decision log trim (2026-09-10)

**D68. How this log gets under the two-thousand-word cap (2026-09-10).** Question: how playbook
step 92's cap is met without breaking the 690 `D<n>` citations that point into this file from
outside it, or the two AGENTS.md rules that every substantive decision, bug and run or debug fact
lands here and that a task citing no paragraph here is not scheduled. Options: (a) one paragraph
per sprint here, every full decision paragraph moved to `docs/DECISIONS_ARCHIVE.md`; (b) every
paragraph rewritten in place to question, recommendation and scope, with the options and evidence
prose dropped; (c) keep the file and record a reasoned exception to step 92; (d) (a), plus the
word cap moved out of prose and into `check-instruction-files.ps1`, the script CI already runs for
the two limits it enforces today. Recommendation: (d). Scopes: Sprint 10, and how every sprint
after it records a decision. Evidence, measured at 0460c95 on 2026-09-10: 23,112 words over 1,866
lines in 91 bold paragraphs, mean 250 words, longest D66 at 1,405; by section, Decisions 7,030,
Sprint 9 6,837, Sprint 7 5,661, Sprint 8 1,817, latency 1,139, starting decisions 305, Current
phase 247, preamble 76. Option (b) cannot reach the cap arithmetically: the preamble and the phase
block already spend 323 words, leaving 1,677 for 91 paragraphs, or 18 words each, and one or two
sentences a paragraph lands near 2,800. Option (c) pays all 23,112 words on every cold read by
every agent, which is the cost the cap exists to stop. Mechanics for the implementing sprint.
Citations are plain text, never links, so a citation resolves by search and needs only that the
archive keeps the `**D<n>. ` heading form; 415 of the 690 are in `src/` and `tests/`. The 14
addenda fold into their parent's archive paragraph. The four run and debug fact paragraphs, 1,825
words, carry no number and so can be cited by nothing, and go to the archive under the sprint that
produced them, as do S1 to S4. Nine lines name `DECISION_LOG.md` by filename and are the whole
rename cost: `README.md:78`, `AGENTS.md:12`, `:38`, `:53`, `DESIGN.md:6`, `:294`,
`FAULT_INJECTION.md:5`, `RETROSPECTIVE_2026-09-06.md:160` and `JsonlRecordReaderTests.cs:148`;
AGENTS.md is at its 150-line cap, so those three lines are edited, never added to. The Current
phase block stays in this file, at its own 20-line cap, and `check-instruction-files.ps1` resolves
`docs/DECISION_LOG.md` before `DECISION_LOG.md`, so the archive must carry neither name. A later
sprint writes its full paragraphs to the archive and at most 120 words here; ten sprint paragraphs
plus the 323 fixed words come to about 1,523, so the cap is reached around Sprint 14, and the
oldest sprint paragraphs merge into one then, the archive already holding them in full. The check
that proves no citation dangles, to be added to `check-instruction-files.ps1` and runnable now:
`$c = (git ls-files src tests docs README.md TalkingPoints.md AGENTS.md) | %{ Select-String $_
-Pattern '\bD[0-9]+\b' -AllMatches } | %{ $_.Matches.Value } | Sort-Object -Unique; $d =
Select-String -Path docs/DECISION_LOG.md,docs/DECISIONS_ARCHIVE.md -Pattern '^\*\*(D[0-9]+)' | %{
$_.Matches[0].Groups[1].Value } | Sort-Object -Unique; $c | ?{ $d -notcontains $_ }`, which must
print nothing. Run on 2026-09-10 against this file alone it printed nothing, over 67 cited and 67
defined. Assumption: none.

**Run and debug fact, D68 executed (2026-09-10).** The trim landed as D68 specifies. This file
is new and holds, verbatim, the 137 paragraphs the log carried below its Current phase section
plus the log's own preamble; `DECISION_LOG.md` keeps its title, its Current phase section byte
for byte, and one paragraph per sprint, and went from 23,647 words over 1,905 lines to 1,060
words over 88 lines. That 23,647 is the working tree the trim ran against, D68's own paragraph
already written into the log; the 23,112 words over 1,866 lines D68 records is the committed
file at 0460c95, before that paragraph was added. One file, two states, 535 words apart. The 14 addenda sit with the paragraph each amends rather than in the order
they were written. `check-instruction-files.ps1` gained the step 92 word cap, defaulting to
2,000, and D68's dangling-citation check; both were proved able to fail before being believed,
the cap by lowering it to 500 and the citation check by running it with this file moved aside,
which made every cited number dangle. At that point the check reported 65 cited numbers against
68 defined and no dangling one; the review fix below moved both counts. Seven of the nine lines
that named `DECISION_LOG.md` by filename now name this file. Two were left as they were,
`AGENTS.md:12` and `DESIGN.md:294`, because each points at the live phase block, which stayed in
the log rather than moving here, and AGENTS.md is at its 150-line cap.
Evidence: `.\check-instruction-files.ps1` exit 0; `dotnet build` clean, 0 warnings; `.\test.ps1`
exit code 0 with 584 tests in `Agent.Tests` and 78 in `Agent.Cli.Tests`, all passing, 100 percent
line, branch and method coverage on both modules, unmoved from the PR #26 numbers above;
`.\sync-agent-rules.ps1` regenerated `.agents/rules/project.md`. The only change under `src/` or
`tests/` is one comment word in `JsonlRecordReaderTests.cs`.

**Run and debug fact, seven review findings on the trim fixed (2026-09-10).** A review of the
Sprint 10 working tree found seven, all in the tooling and the prose the trim touched. One:
`check-instruction-files.ps1` took a definition from `^\*\*(D[0-9]+)`, which also matches the
log's sprint headings, so `**D1 to D31,` at `DECISION_LOG.md:43` registered D1 as defined and the
five headings below it did the same for D32, D38, D57, D60 and D68; for those six numbers the arm
could not see a dangling citation, while the script's own header claimed every cited number
resolves to a paragraph. Requiring a trailing period instead gives 67 of 68, since `**D67 (open).`
is not `**D67.`. The rule now reads definitions from this file alone, the log holding no decision
paragraph after the trim, and only from a heading of the form `**D<n>.` or `**S<n>.` with
` (open)` the one permitted insert. An addendum or open-question heading amends a paragraph
rather than defining one and does not count; all 68 D numbers and all 4 S numbers still resolve
without them. Proved on a fixture repository, this repo's `.git` copied beside `AGENTS.md`, the
log and an archive with the `**D1.` paragraph deleted while the log's range heading stayed: the
old script exits 0, the new one exits 1 naming D1. Two: S1 to S4 live only here, and S2 is cited
from `ComposedMessagePayload.cs:5`, `OpenAiMessageComposer.cs:31`, `:170` and `:234` and
`OpenAiMessageComposerTests.cs:489`, but the pattern was `\bD[0-9]+\b` and the log's preamble
stated the resolution rule for the D form only, so the S namespace was neither gated nor
documented. Both now cover D and S, and the same fixture with the `**S2.` paragraph deleted exits
0 on the old script and 1 on the new. Three: this file is untracked, so `git ls-files` did not
list it and the citations it makes itself went unscanned. It is now in the scanned set whether or
not it is tracked, which moved the reported counts from 65 cited against 68 defined to 72 against
72, being 68 D numbers and 4 S numbers, none dangling. Four: a deleted archive came out as 57
dangling citations and never as an absent file. The arm now names the absence and checks no
citation, proved on a second fixture built without this file. Five: `DESIGN.md:146` named the
decision log for S1 to S4 and `:308` cited S1 to S4 and D8 to D12 without saying where they are;
both now name the archive. D68's nine-line inventory missed them because it counted literal
filename occurrences only. Six: `AGENTS.md:56` described the check as two rules when it enforces
four, and `DESIGN.md:8` and `:291` still pointed decisions at the log. All three now read as the
tools behave, and the AGENTS.md bullet was rewrapped inside its existing three lines, at 111, 118
and 94 characters against that file's own longest line of 119, so nothing was cut to make room
and the file stays at 150 lines. Seven: the two baselines and the eight-of-nine count in the
paragraph above are corrected in place. Evidence: `.\check-instruction-files.ps1` exit 0,
reporting 150 lines, 1,111 words of 2,000 and 72 cited numbers resolving among 72;
`.\sync-agent-rules.ps1` regenerated `.agents/rules/project.md` at 10,410 bytes; `dotnet build`
clean with 0 warnings; `.\test.ps1` exit code 0 with 584 tests in `Agent.Tests` and 78 in
`Agent.Cli.Tests`, all passing, 100 percent line, branch and method coverage on both modules,
unmoved. Nothing under `src/` or `tests/` changed.

## Sprint 11 decisions, the Phase 7 evidence (2026-09-10)

**D69. Where the committed scorecard lives (2026-09-10).** Question: Phase 7's check asks for
the scorecard on the synthetic set as a file in the repo, and today `--eval-report` writes it
only where `.gitignore` excludes it (`/eval*.txt` at the root), so no documented run leaves one
behind. Options: (a) the CLI writes it straight into a tracked folder, `docs/scorecards/`, one
file per set and composer, never edited by hand; (b) un-ignore the root report, so a tracked file
sits where every documented run in OPERATIONS.md overwrites it and dirties the tree; (c) paste
the table into DESIGN.md section 9, a hand copy that can drift from what the CLI printed and is a
section rather than a file; (d) (a), plus a golden test that regenerates the file and fails on
any difference. Recommendation: (a). Option (d) cannot hold as stated: two offline runs of the
synthetic set on 2026-09-10 at 7d7bb17 wrote byte-identical output files and scorecards that
differed only on wall clock, record 2 at 19 ms against 20 ms, p95 19 against 20 and batch 29
against 30, so a golden either fails on noise or masks the latency cells, and every tally it
would pin `BaselineNumbersTests` already pins. The file is therefore a dated snapshot: DESIGN.md
section 9 names the command, the commit and the date that wrote it, and a sprint that moves a
tally on that set rewrites it. Scopes: `docs/scorecards/`, DESIGN.md section 9, and the variance
report of D70, which is built from files of the same kind. Evidence: the two runs above.
Assumption: none.

**D70. What the variance report measures (2026-09-10).** Question: playbook step 83 says
to run the synthetic set with the real model three times and report the variance. D31 measured
that on these sets the model answers nothing: the call timeout is derived from the strictest
stated `p95_latency_ms`, 2000 ms, split into two 1000 ms attempts (D28), and no completion of
this size returned inside 1000 ms on any of 46 calls. Options: (a) run `--composer openai` three
times as the product stands, where every record falls back to the template, so every check
column is identical across runs by construction and only latency and token counts vary: the
variance of the shipped configuration, which says nothing about the model's prose; (b) take
D31's second way out, which is D36's third option, a documented flag that sets the per-call
timeout for an evaluation run in place of the budget-derived one, built test-first, then three
runs, so the model answers and the report measures what step 83 asks about, and the first
successful call is also D32's measurement; (c) take D36's second option, `p95_latency_ms` read as
a reporting threshold with the timeout configured separately, which reverses D28 for every run
rather than for evaluation runs and ships a configuration that fails the p95 check on purpose.
Recommendation: (b), weakly over (a). Option (a) costs no code and is honest, but three runs that
cannot differ measure the fallback, which FAULT_INJECTION.md section 1 already proves. Under any
option the report is `docs/VARIANCE.md`, written by hand from the three runs' scorecards in
`docs/scorecards/` and their diagnostics, with no new code for the report itself. Cost of (b),
estimated and not measured: D31's addendum priced 46 abandoned calls at about $0.004 with roughly
a third of them billed; three runs over the ten records that get a message are about 30 to 60
calls billed in full at `gpt-4o-mini`, on the order of a cent. Options (b) and (c) close D31's
open question and D36; (a) leaves both open. The judge (`--judge`, `gpt-4o`) is out of all three,
since step 83 does not ask for its variance. Needs the requester: D31 reserves (b) and (c) for
the requester as product changes, and every option spends on the requester's key and sends the
synthetic records to the vendor under D44's retention. Scopes: `docs/VARIANCE.md`,
`docs/scorecards/`, and under (b) `CliRunner`, `ModelCallBudget`, OPERATIONS.md, D28, D31, D32
and D36. Evidence: D31 and its addendum. Assumptions: A15, A18. Taken 2026-09-10 by the
requester, whose answer was to get the model runs working first and settle how many runs the
report needs afterward, so step 83's count of three waits on a working run. Option (b), in this
form: `--model-call-budget-ms <n>` replaces the budget D28 derives from the records, for the
composer's model calls only, where `n` is a positive whole number of milliseconds. The flag is
refused with exit code 1 when `n` is not one, and when the composer is not `openai`, because on
any other composer it would bound nothing, and a flag that silently does nothing is a flag
someone trusts. The scorecard's p95 check keeps the records' own stated budget, so a run under
the flag reports its p95 against 2000 ms and fails it, which is D36's cost stated rather than
hidden. The judge keeps its own default. D32's measurement is the run and debug fact at the end
of this section, taken with a scratch copy of the set before the flag existed. This closes D31's
open question and D36 for evaluation runs only; D33 to D35 and D37 stay unscheduled.

**D71. What the scorecard says about an input line that did not parse (2026-09-10).**
Question: step 82 is to read every scorecard row. The synthetic set's scorecard, run 2026-09-10
at 7d7bb17, has 12 rows and reads `Overall: 12/12 passed`, and it says nothing about line 11,
which is malformed by design (DESIGN.md section 4, item 10). The line is reported, but
elsewhere: `JsonlRecordReader.ReadAll` returns a failure for it, the error stream and the log
carry `Line 11 failed to parse`, the log's `Batch complete` line reads `13 record(s), 1
failure(s)`, and the exit code is 2. None of that reaches the scorecard, and the scorecard is the
file D69 commits, so a reader of that file alone sees a clean 12 of 12 for a 13-line input.
Options: (a) the scorecard gains one line naming every input line that did not parse, by line
number and never by content, with `Overall` still stated over the records that parsed; (b) the
same line, with `Overall` stated over every line read; (c) a table row per unparsed line reading
`n/a` across; (d) leave the scorecard and state beside the committed file that the exit code and
the error stream carry it. Recommendation: (a). A line that did not parse has no `expected` that
could be read, so it can be neither passed nor failed, and counting it in `Overall` (b) would
lower a tally `BaselineNumbersTests` pins for a line no check measured; a row (c) puts a task id
column on the one thing whose task id is what failed to parse; (d) keeps the fact out of the only
file the check names. Under (a) no pinned tally moves. `--replay` reads `--input` through the
same reader, so the line applies there too. Scopes: `Scorecard` and its formatter, `CliRunner`
where the parse failures are known, the `--eval-report` row of OPERATIONS.md, and the committed
scorecard of D69, which is written after this is settled. Evidence: the run above and its error
stream. Assumption: none. Taken 2026-09-10 by the requester, in a form none of the four options
named: a line that did not parse is a failure, and the scorecard says so. It becomes one row
reading `(did not parse)` in the task id column, because its task id is what failed to parse,
and `ERROR:` plus the reader's own failure text in the result column, which names the line
number and a byte position and never the line's content (step 68). That is the form an
unscoreable record already takes (step 34), so the row counts in `Overall`, never passes and
measures no check, which leaves every per-check tally where it was; it is the split JUnit's XML
report keeps between an error and a failure, both counted among the tests. The synthetic set
now reads `Overall: 12/13 passed`, and `BaselineNumbersTests` pins that drop deliberately, on
this decision. The same reading found a second gap of the same kind: a record that throws inside
the agent leaves the batch loop by a `continue` and is missing from the scorecard too, so it gets
the same row under its own task id, its reason redacted to the exception type as D46 requires.
The rows are appended after the judge, which pairs rows with runs by position, through
`Scorecard.AppendUnprocessed`, which builds a new scorecard rather than copying one; its three
callers are the run path, the replay path and `BaselineNumbersTests`.

**Run and debug fact, the first model calls that answered (2026-09-10).** D32's measurement,
taken before any code changed. A scratch copy of `synthetic_12.jsonl` with every
`p95_latency_ms` raised from 2000 to 30000, kept outside the repo and never committed, was run
at 7d7bb17 with `--composer openai --now 2026-03-07T12:00:00Z`, so D28 gave each call 30000 ms
and each attempt 15000 ms. Exit code 2, for line 11. The model answered: 19 calls, 19 completed,
7,479 input and 2,135 output tokens, zero network retries. Of the ten records that get a
message, seven read `composition.composer: openai` (one at `attempts` 1, six at 2) and three read
`template` at 3, after both model drafts were rejected (`synthetic_02`, `_04`, `_12`). What one
completion costs in wall clock: `synthetic_13`, the one record that made a single call, took
1,855 ms end to end; the two-call records took 4,467 to 8,302 ms, about 2.2 to 4.2 s a call.
No completion fits D28's 1000 ms attempt, and few would fit the whole 2000 ms, which is D35's
second case: no division scheme helps, and D36 is the real question. The scorecard: CTA 9 of 10
(`synthetic_05` FAIL), every other check unmoved, 11 of 12 overall, p95 9,218 ms against the
file's 30000. Three findings the run opened, none a decision yet: the first model draft failed
the safety gate for `Missing required opt-out instructions` on nine of the ten records, every
one but `synthetic_13`; `synthetic_05`'s call-to-action type is wrong under the model and right
under the template; and brand style reported `ExclamationLimit` on `_03`, `_05` and `_09`.
Money was not read from the vendor's usage page, so none is stated.

**Run and debug fact, D70 and D71 executed (2026-09-10).** Test-first: eight new CLI tests
failed before the change, the four invalid budgets and the template-composer refusal because the
flag did not exist and the three scorecard tests because no `ERROR` row was printed; the no-key
guard test passed before and after, as a rule the change had to keep; and the library tests
failed to compile on exactly the three missing members, `RecordScore.DidNotParse`,
`Scorecard.AppendUnprocessed` and the two-argument `ModelCallBudget.PerCallBudget`. After:
`.\test.ps1` exit code 0, 587 tests in `Agent.Tests` and 87 in `Agent.Cli.Tests`, all passing,
100 percent line, branch and method coverage on both modules; `check-instruction-files.ps1` exit
0 over 75 cited numbers. The two committed scorecards, both written by the CLI from the Sprint 11
working tree on 7d7bb17 with `--now 2026-03-07T12:00:00Z` and never edited:
`docs/scorecards/synthetic_12_template.txt`, every check unmoved, `Overall: 12/13 passed`, p95
19 ms, exit 2; and `docs/scorecards/synthetic_12_openai_run1.txt`, with `--composer openai
--model-call-budget-ms 30000`, exit 2, 19 calls, 19 completed, 7,479 input and 2,129 output
tokens, eight of the ten messages written by the model (`synthetic_13` at one attempt, seven at
two) and two by the template after both drafts were rejected (`synthetic_02`, `_04`), CTA 9 of 10
(`synthetic_05`), p95 8,043 ms against 2000 ms and so FAIL, `Overall: 11/13 passed`. Against the
scratch run earlier the same day, `synthetic_12` moved from the template to the model, the first
run-to-run variance this project has observed; eleven drafts across the run were rejected, every
one for `Missing required opt-out instructions`. One cosmetic effect of D71: the formatter pads
the last column to its widest cell, so every row of a scorecard with an `ERROR` row carries
trailing spaces to the width of the reader's failure text, as an unscoreable row always did.
Nothing is pinned for the live run: its prose, its latency and its token counts are the vendor's.

**D72. The model's drafts refused for missing opt-out instructions (2026-09-10).**
Question: across the three runs of VARIANCE.md the safety gate refused 33 of the model's drafts,
11, 10 and 12, every one for `Missing required opt-out instructions`, and the first draft failed
on nine of the ten records on every run. The prompt says only `Opt-out instructions: required.`
(`OpenAiMessageComposer.BuildUserPrompt`), while the gate accepts a narrow definition
(`OptOutInstructions`: STOP in capitals, `reply stop` or `text stop`, `opt out`, `opt-out`,
`unsubscribe`); the corrective retry carries the violation reason and usually passes, at the
cost of a second call on almost every record. `synthetic_04`, the Spanish record, was refused on
both attempts on all three runs and the template wrote it each time. Why is not read, because no
artifact carries a draft refused inside the compose-validate loop: the review queue holds only
what the final gate refuses (D43) and a log line never carries prose (step 68). Options: (a) state
the accepted forms in the prompt, which is prompt text and a golden-test change and still leaves
the model to comply; (b) have code append the opt-out sentence after the model writes, from the
per-language sets the template composer already uses, so a required disclosure is owned by code
the way the link is (S2); (c) leave it and pay the second call. Recommendation: (b), because a
required disclosure is a reproducible decision and S2 gives those to code, and it removes the
refusal rather than making it rarer; its effect is measured by rerunning VARIANCE.md's three
runs. Scopes: `OpenAiMessageComposer`, the language sets, the prompt's golden test, VARIANCE.md.
Evidence: VARIANCE.md and the three runs' error streams. Assumption: A18. Taken 2026-09-10 by
the requester ("fix the issues. dont defer"), option (b): when a record requires opt-out
instructions and the model's body carries none by `OptOutInstructions`, the composer appends the
record's own language set's sentence, `SmsOptOut` after a space or `EmailOptOut` on its own line,
the sentence the template writes; a body that already carries one is left as the model wrote it,
so none is doubled. The prompt says `Opt-out instructions: the system appends them, so do not
write any.` where it said `required`. A language with no set gets the English sentence, the same
fallback the template takes with `LocaleApplied` false. `OptOutInstructions` stays the one
definition: the composer reads it, the first reference from Composition into Safety.

**D73. The model's call to action on a record that states none (2026-09-10).** Question:
`synthetic_05` states no `primary_cta`, the prompt then says `No specific call to action is
required; choose one reasonable for this message.`, and the model chose `contact` on both runs
where it wrote the message. The label expects `reply`, which is what the template writes under
A9's generic call to action, so the one scored check that varied across VARIANCE.md's three runs
passed only when both model drafts were refused and the fallback answered. Options: (a) name
A9's generic type in the prompt when the record states none; (b) have code set the type from A9
and leave the model only the options prose; (c) leave it, since the label is one reading of an
unstated constraint. Recommendation: (b), keyed on the absence of the input field as A9 already
is and not on this label, which D9 forbids fitting to: the type is a decision, and S2 gives
decisions to code. Scopes: `OpenAiMessageComposer`'s call-to-action instruction and its response
schema. Evidence: VARIANCE.md's per-record table. Assumption: A9. Taken 2026-09-10, option
(b): the required type is `CallToActionCatalog.Resolve(primary_cta).Type` on every record, so an
absent or blank `primary_cta` requires A9's `reply` as the template does, the schema's enum is set
on every call, and a model returning any other type is a failure the compose-validate loop
retries. The email link follows the resolved call to action's own path, so
`CallToActionCatalog.LinkPathForType`, which existed only because the type sent could diverge from
the constraint, lost its one caller and is deleted.

**Run and debug fact, step 83's three runs and the Phase 7 close (2026-09-10).** Runs 2 and 3
were made at e4c74ad with run 1's command, changing only the scorecard name, and wrote
`docs/scorecards/synthetic_12_openai_run2.txt` and `_run3.txt`; both exited 2, for line 11.
Their numbers and run 1's are tabled in VARIANCE.md, written by hand from the three scorecards
and the three runs' diagnostics and output files, which were read from the scratchpad and not
committed. With it, Phase 7's check holds: the synthetic scorecard, the variance report and the
fault-injection results are all files in the repo. Steps 86, 87 and 88 were not done and are
recorded as owed in the log's Current phase block. No code changed for the close.

**Run and debug fact, D72 and D73 executed (2026-09-10).** Test-first: nine composer tests failed
against the unchanged code, the four appended-sentence cases, the prompt's new opt-out line, the
golden prompt, the generic type in the prompt and in the schema, and the blank `primary_cta`;
the other 44 passed. After: `.\test.ps1` exit code 0, 592 tests in `Agent.Tests` and 87 in
`Agent.Cli.Tests`, all passing, 100 percent line, branch and method coverage on both modules.
Three live runs with run 1's command wrote `docs/scorecards/synthetic_12_openai_after_d72_run1.txt`
to `_run3.txt`: 12 of 13 and CTA 10 of 10 on every run, 10 calls and 3,900 input tokens each,
976, 990 and 975 output tokens, no draft refused, the model writing all ten messages at one
attempt each, p95 2,318, 2,398 and 4,508 ms against 2000 ms and so still FAIL. Against Sprint
11's first three runs: refusals 33 to 0, calls 57 to 30, output tokens 6,308 to 2,941. Brand
style reported 4 findings a run, a diagnostic (D39). VARIANCE.md carries both sets of runs.

**Run and debug fact, playbook steps 87 and 88 (2026-09-10).** `docs/RUNBOOK.md`, 42 lines: set
the secret, build and test, run both documented sets, open the scorecard, read a log line, replay.
Step 88 cloned the branch fresh into a temporary directory and ran the runbook's text alone:
`dotnet build` exit 0 with 0 warnings; `.\test.ps1` exit 0, 592 and 87 tests, 100 percent; the
hold-out run exit 0 reading `Overall: 4/12 passed`; the synthetic run exit 2 reading `Overall:
12/13 passed`; `--replay` exit 0 with the same tallies but `Safety 0/0` and latency `n/a`, as the
runbook says; `git status` in the clone empty, so every file the runs wrote is ignored. No step
needed help, so the run produced no finding. The runbook's claim that `run.log` is appended to by
every run was not exercised by that run and was checked in code instead: `FileLoggerProvider`
opens its writer with `append: true`.

**Run and debug fact, playbook step 86 (2026-09-10).** A read-only review of every test, 511
methods in `Agent.Tests` and 84 in `Agent.Cli.Tests`, recommended deleting 38 that assert no rule
a reader could dispute (a duplicate of another test's rule and branch, an echo of the input, a
bare "does not throw") and strengthening 10 whose assertion a wrong implementation would also
pass. 33 deletions and 8 rewrites landed in one commit, and the coverage gate is the proof each
deletion was safe: `.\test.ps1` exit 0 at 562 and 86 tests with 100 percent line, branch and
method coverage on both modules, and no deletion had to be restored. Every stronger assertion
passed. One recommendation was declined: `ProspectCase_WithExpectedPresent_RoundTripsThroughSerializeAndDeserialize`
stays, because it is the only coverage of `LenientExpectedOutcomeConverter.Write`, which
`JsonConverter` requires, and it pins that a serialized record keeps its `expected` block. One
finding outside the step was checked and found to be decided already: `CliRunner` logs a failed
record's exception attached, stack trace included, while the scorecard row carries the type alone.
D46 classed a per-record bug's message as program-authored and OPERATIONS.md sends an operator to
that stack trace for exit code 2, and vendor, model and record text is caught inside the
composer and the readers before any record-level catch, so the line stays as D46 left it.

**Run and debug fact, the combined branch and D35's measurement (2026-09-10).** The four agent
branches were cherry-picked onto `sprint-11-phase-7-evidence`, and the rest of step 86 landed
there: five more deletions in `CliRunnerTests.cs` and `ModelCallBudgetTests.cs`, and the two
tests that built the model composer now assert the model the CLI resolved, through a new log line,
`Composer: openai, model <name>.`, after failing on its absence first. `.\test.ps1` on the combined
branch: exit 0, 564 tests in `Agent.Tests` and 85 in `Agent.Cli.Tests`, 100 percent line, branch
and method coverage on both modules. Then one live run of `synthetic_12.jsonl` with `--composer
openai` and no `--model-call-budget-ms`, to measure what D33 and D35 bought on the records' own
2000 ms: the model wrote 2 of 10 messages where the step 60 run's model wrote none of 23, 10
calls with 2 completed, 792 input and 186 output tokens, 12 of 13, p95 2,066 ms and so FAIL,
batch latency 6,072 ms under D37's four records at once against the step 60 run's 63 seconds.
Scorecard `docs/scorecards/synthetic_12_openai_no_flag_after_d35.txt`; VARIANCE.md tables it.

**Run and debug fact, the PR #28 review fixes (2026-09-10).** Commit e852939 fixed three findings
from the PR #28 review before the merge. `CliRunner.RunRecordAsync` no longer catches
`OperationCanceledException` as a record failure, matching `LeasingMessageAgent.RunAsync`'s own
exclusion; under D37's concurrency one cancellation could otherwise log up to four spurious
`Record failed.` entries, and `CancelsWhileComposingComposer` is the test substitute that proves
it. `ValidatingMessageComposer`'s three exits share one `CombinedSpend` helper for adding a
discarded attempt's cost and retries to the outcome's own, where each exit had repeated the same
two lines. `Scorecard.AppendUnprocessed`'s comment states its real cost, a full recompute of the
p95 and the tallies, where it had said O(n + m). No archive entry was written when it landed; the
step 92 audit of 2026-09-10 found the gap and this paragraph closes it.

**Run and debug fact, Sprint 12's steps run at once (2026-09-10).** Playbook steps 90 (the
README), 91 (DESIGN.md), 92 and 96 (an audit of the decision log and the instruction files) and
95 (secrets, personal paths and material not the owner's) ran as four agents at the same time,
at the owner's request. That departs from Phase First's step order, and was taken because the two
editing agents wrote different files and the two auditing agents wrote none. Every change was then
reviewed against the code and the committed evidence rather than against the agents' reports:
the pipeline order against `LeasingMessageAgent` (select line 70, plan 89, compose 111, schedule
147, validate 155); every decision number the rules table now cites against its heading; every
test substitute in the new interface table against its class; every line citation
(`BaselineNumbersTests` lines 27 and 33, the template scorecard's line 14, `NextActionPlanner`
line 10, `CliRunner` line 54); and every model-path number against VARIANCE.md. Two findings, both
in the README and both fixed: it said twenty-one assumptions after DESIGN.md had added A22 and
A23, and it named no test command, because its agent was barred from running one. `.\test.ps1`
exit 0 with 100 percent line, branch and method coverage; `check-instruction-files.ps1` exit 0.
The README is 101 source lines, 15 of them the diagram; its height as rendered was not measured
against step 90's two screens.

**Run and debug fact, playbook step 95 (2026-09-10).** A read-only search of all 150 commits on
every branch and the 214 tracked files found no secret (key, token, private key and
connection-string shapes, and any key or token name given a quoted literal), no committed
`.env`, `secrets.json` or `appsettings` file, no machine path or machine name, no email address or
phone number, and no pay figure; every author uses GitHub's no-reply address, and the run outputs
on disk are ignored and were never committed. `UserSecretsId` in `Agent.Cli.csproj` is an id, not
a secret. It found the repository public, the assignment's text and data committed (D74), and
low-severity mentions of the interview in `docs/RETROSPECTIVE_2026-09-06.md`, `TalkingPoints.md`,
`AGENTS.md`, `README.md`'s history and two commit messages, which D74 keeps as they are.

**D74. The assignment's own material in a public repository (2026-09-10).** Question: step 95
removes material that is not the owner's to publish, and `AndrewVanDelden/RealPagesInterview` is
public, carrying the assignment's text `problem_statement.txt` and its data `sample.jsonl` since
commit 06c0ebd and `holdout_12.jsonl` since edf25c3 (D11). Options: make the repository private,
instant and reversible; replace the three files with the project's own data and rewrite history,
which breaks the eight source and test files that name them, force-pushes every branch, and
still leaves the old commits under the 28 pull request refs GitHub keeps; or keep it public as it
is. Recommendation: private. Taken 2026-09-10 by the owner: public as it is, the one exception
step 95 records. Scopes: step 95; no file changes. Evidence: the step 95 run and debug fact above.
Assumptions: none.

**D75. The step 96 changes to the instruction files (2026-09-10).** Question: which of the
step 96 audit's findings become changes. `AGENTS.md` is 150 lines, which the check script passes
because it tests greater than 150, and step 96's "under 150" does not. Options: (a) trim the
passages the code already states (the Layout folder list, the `AgentJsonOptions` sentence, the
output array, `ReadAll`, the `ILogger` default, `CommunicationChannel`, `ActionCatalog.Create`, the
`Scorecard` copy, the `CompositionNotes` shape) and move the `SlotResolution` note to
`docs/CODE_REVIEW.md`; add two negative rules for mistakes that repeated, spend dropped on an exit
that did not produce the message (the D28 addendum, D66, D67 and a run and debug fact of
2026-09-10) and a rename or delete whose sweep missed prose or cited the wrong decision (three run
and debug facts of 2026-09-09 and 2026-09-10); correct the two stale lines, "All work on `dev`,
never `main`" while work lands on sprint branches merged into `dev`, and "four rules" followed by
five; and have the script enforce the 120-word paragraph cap (D68), the four-line phase section of
step 92, and under 150 lines; (b) the two rules and the corrections only; (c) none.
Recommendation: (a). Taken 2026-09-10 by the owner: (c), none. `AGENTS.md` stays at 150 lines
with the two stale lines as they are, and the script's limits stay as they are, so step 96's
"under 150" is an exception on record rather than a pass. Scopes: Sprint 12, step 96. Evidence:
the step 96 audit of 2026-09-10. Assumptions: none.

**D76. What step 93's final diff is (2026-09-10).** Question: step 93 runs the automated review
on "the final diff", and PRs #1 to #28 were each reviewed on their own. Options: this sprint's
diff into `dev`, the one part of the release no review has read; or the whole release, `dev`
against `main`, which `main`'s single README commit makes the entire project. Recommendation:
the first, since a whole-project review is `/code-review ultra`, which only the owner can launch.
Taken 2026-09-10 by Claude as the stated default when the owner said to go on to step 93.
Scopes: step 93. Evidence: `gh pr view 28` shows four reviews posted from the owner's account and
no bot; `.github/workflows/test.yml` is the only workflow. Assumptions: none.

**Run and debug fact, playbook step 93 (2026-09-10).** The Claude Code review ran at high over
`dev...sprint-12-documentation` and the working tree, a docs-only diff: `README.md`,
`docs/DESIGN.md`, this log and this archive. Each claim the diff adds was traced to its source:
D42's third brand rule is `SubjectMatchesChannel`; `ValidatingMessageComposer` retries only a
safety rejection, with the violations fed back, sends any other failure straight to the fallback,
and validates the fallback; `OpenAiCompletionClient` retries once on 408, 429, 500, 502, 503 or
504 and never after a timeout; D70 leaves the judge out of every option and closes D36 for
evaluation runs only; three hold-out records assert `renewal_offer_loaded`; `interface I` matches
three files in `src/`; DESIGN.md section 9 records batch p95s of 19 to 22 ms. Findings: none.
The second reviewer `docs/CODE_REVIEW.md` names, Gemini under Antigravity, is run by the owner
on the Sprint 12 PR and has not run.

**Run and debug fact, playbook steps 93, 94, 97 and 98 and the Phase 8 check (2026-09-10).** The
owner marked step 93 complete with its second reviewer, Gemini under Antigravity, not run: PR #29
merged with no review and no comment on GitHub. Step 94 is the owner's read: `docs/RELEASE_v1.0.0.md` names nine
files, `SafetyValidator.cs`, `SafetyTextNormalizer.cs`,
`OptOutInstructions.cs` and `ValidatingMessageComposer.cs` under `src/Agent/Safety/`,
`ChannelSelector.cs`, `LeasingMessageAgent.cs`, `OpenAiMessageComposer.cs`,
`ExceptionFormatting.cs`, and the two key reads in `CliRunner.cs`. Step 97: the annotated tag
`v1.0.0` is on `ccf3c5c`, the PR #29 merge into `dev`, and the narrative is
`docs/RELEASE_v1.0.0.md`, because the release PR could not open (D78).
Step 98: `docs/NARRATION.md` opens with a 150-word script that answers all seven questions in one
minute spoken, written in the owner's voice at the owner's request; the delivery aloud is the
owner's. The rehearsal was rerun at the tag and every value it states matched. The check ran from
a worktree at `v1.0.0`: `dotnet build` 0 warnings and 0 errors; `.\test.ps1` exit 0, 86 and 564
tests at 100 percent line, branch and method coverage; the README run command exit 0;
`sample.jsonl` at `Overall: 2/2 passed`; the runbook's hold-out run exit 0 at
`Overall: 4/12 passed`, its synthetic run exit 2 at `Overall: 12/13 passed`, a
`TaskId=prospect_welcome_day0` line in `run.log`, and its replay exit 0 with the same tallies and
Safety 0/0. Phase 8's check passed.

**D77. Whether Phase 9 is already met (proposed 2026-09-10).** Question: playbook step 99 runs
the real judged set with no code touched, and this project's judged set was `holdout_12.jsonl`,
run and read in `docs/RETROSPECTIVE_2026-09-06.md`, which took steps 100 to 102 that day. The
product was then rebuilt from that retrospective's decisions, so step 103 counts the hold-out as
seen, although D9 fits no rule to it, and no unseen judged set exists. Options: (a) the
retrospective of 2026-09-06 meets Phase 9, `synthetic_12.jsonl`, frozen since 2026-09-08,
carries the honest number, and step 104, updating the playbook from this project, is what
remains; (b) a fresh judged set labeled by someone other than its author, and steps 99 to 104 run
against `v1.0.0`; (c) close the project at Phase 8. Recommendation: (a), since the interview is
over and (b) needs a labeler the project does not have. Scopes: Phase 9, steps 99 to 104.
Evidence: `docs/RETROSPECTIVE_2026-09-06.md`; DESIGN.md section 4. Assumptions: none. Taken
2026-09-10 by the owner: (a).

**D78. Where the release narrative is published (proposed 2026-09-10).** Question: step 97 writes
the narrative as the release PR's description, and `gh pr create --base main --head dev` failed
with "The dev branch has no history in common with main": `main` holds one commit, `a7a76ae
Initial commit`, a README, and the repository's default branch is `dev`. Options: (a) a GitHub
release on the tag `v1.0.0` whose notes are `docs/RELEASE_v1.0.0.md`, leaving `main` as it is;
(b) a branch joining the two histories with `--allow-unrelated-histories`, the README conflict
taken from `dev`, and a PR from it into `main`; (c) the file in the repository and nothing
published. Recommendation: (a), since `dev` is what a visitor already sees and a release is where
a tag's narrative lives on GitHub; publishing it is the owner's call. Scopes: step 97. Evidence:
the failed `gh pr create`; `gh repo view` reads `dev` as the default branch. Assumptions: none.
Taken 2026-09-10 by the owner: (a). The release is
https://github.com/AndrewVanDelden/RealPagesInterview/releases/tag/v1.0.0, published with
`gh release create v1.0.0 --verify-tag --notes-file docs/RELEASE_v1.0.0.md`, not a draft and not
a prerelease.

**Run and debug fact, the end of Phase 8 (2026-09-10).** The owner reported steps 94 and 98 done:
the nine files `docs/RELEASE_v1.0.0.md` names were read by hand, and the one-minute script in
`docs/NARRATION.md` was delivered aloud. PR #30 merged into `dev` as `9461f4f`. With D78 taken and
the release published, every Phase 8 step is done or an exception on record (step 93's second
reviewer, D75's 150 lines), and the check passed at `v1.0.0`.

**Run and debug fact, playbook step 101 (2026-09-10).** Section 10 of
`docs/RETROSPECTIVE_2026-09-06.md` maps each of its sixteen findings to the playbook step whose
text would have caught it, with the first commit of that clause in `~/.agent-rules` found by
`git log -S`: fourteen map to existing steps; finding 7, two components answering one question,
maps to no step and names a new one; finding 8 is declined by D55. No commit shows playbook text
on 2026-09-04, the build day; the root commit `be974c4` is dated 2026-09-05. The mapping also
found D55 citing D43 for the orchestrator's own gate, which D43 does not mention; that correction
is a separate task. Phase 9's check passed.

**D79. Which step 104 changes land in the playbook (proposed 2026-09-10).** Question: step 104
adds a step for every failure no step would have caught and cuts every step that never earned its
place, in `~/.agent-rules/PROJECT_PLAYBOOK.md`, the owner's repository. A read-only audit of this
archive from 2026-09-07 on, plus section 10 of the retrospective, proposes eight additions and
four cuts or rewrites. Additions: finding 7's step, one owner per question, a value two
components compute being a finding; a Phase 6 step after 75, test every count per unit of work on
every exit, refusal, failure and fallback included, and in the test substitutes (the D28
addendum, D66, D67); step 49 amended to measure one real call before setting a timeout and to
bound the whole call with its retries (the D28 addendum's 4828 ms, D31, D32); step 25 amended to
cover every collection's elements (D47); step 71 amended to drive one violating record through
the production wiring before reporting a zero (D48); step 21 amended to prove each hook fails on
a planted violation of every rule it claims (the D68 review fact); a step beside 22 to grep code,
tests and docs for any deleted, renamed or renumbered name and number (the Sprint 8 and D68
facts); step 16 amended to confirm `git merge-base main dev` returns a commit (D78). Cuts or
rewrites: step 81 to confirm the artifacts and exit codes and read one record across them, field
comparison being step 28's (D63); step 88 cut where CI builds a fresh clone on every push (the
step 88 run and debug fact, which found nothing); step 93 met when no diff escaped a step 22
review (D76); step 31 amended to schedule the judge on a scorecard a phase check commits, or not
to build it (every committed scorecard reads `ActionSem 0/0, BodySem 0/0`). Options: (a) all
twelve; (b) the eight additions and no cuts; (c) finding 7's step only; (d) none.
Recommendation: (b), since each addition has a failure behind it while each cut rests on one
project, and the cut rule is better applied when a second project shows the same. Scopes: step
104; `~/.agent-rules` only, no file in this repository. Evidence: the citations above.
Assumptions: none. Taken 2026-09-10 by the owner: (d), none. The rules stay as they are; the
twelve items stand only as this project's record, and step 104 is closed with no change.

**Run and debug fact, the narration checked against the code (2026-09-10).** One record,
`prospect_welcome_day0`, was traced through the code file by file with the narration read beside
it, and all three sets were re-run at the tag with the diagnostics and the evaluation report on.
Every value reproduced. Eight statements did not match the code or the artifacts and were fixed in
`docs/NARRATION.md`: the run artifacts it cited were written fifteen hours before the tag; the SMS
reply options were attributed to the call-to-action table rather than the English prose set; the
ingest notes were attributed to the reader rather than the runner; step 6 was described as writing
the row per record when the array is written once after the batch, and "every check reads OK"
when the two judge columns read not measured; curveball five counted a no-branch miss among the
no-match misses; two of the three interfaces were said to have an offline implementation in the
product when their substitutes are in the tests; the horizon rule was said to be under 45 days when
it is at most 45; and question 5 gave one exit of the three the template asks for.

**D80. Whether `TalkingPoints.md` stays in the repository (proposed 2026-09-10).** Question: the
root holds `TalkingPoints.md`, 147 lines written in Sprint 8 as the spoken script before
`docs/NARRATION.md` existed, and the narration now answers the same seven questions from the
playbook template, so the two files say the same thing twice and only one is kept current.
Options: (a) delete it, and the retrospective and this archive keep naming it as what was true on
their dates; (b) delete it and rewrite every document that names it; (c) keep it as an archive.
Recommendation: (a), since a retrospective and a decision record are not rewritten after the fact,
while `docs/OPERATIONS.md` is a live reference and gets one clause pointing at the git history
instead. Scopes: the root file; one clause in `docs/OPERATIONS.md`; its name removed from the scan
roots of `check-instruction-files.ps1`, which already skipped a path that does not exist; four code
comments in `src` and `tests` that cite its Sprint 7 and Sprint 8 history stay, since `git log --
TalkingPoints.md` still serves that history. Evidence:
the two files side by side on 2026-09-10; the git history keeps every version. Assumptions: none.
Taken 2026-09-10 by the owner: (a).

**Run and debug fact, the architecture score (2026-09-10).** A read of the orchestrator, ingest,
decisions, composition, safety, evaluator, the runner and the docs scored the system 7 of 10:
structure 9, determinism 9, failure handling 8, verification 8, domain coverage 4,
maintainability 5, scalability 6. One finding was withdrawn on evidence: the constant
`consent_verified` was read as a state that carries no information, but the hold-out record
`resident_opt_out_respected` asserts that state and expects `no_op`, so the label means "consent
was checked" and the constant states exactly that. The two custom logger providers were read and
kept: `AddConsole` writes to the real console and cannot be redirected to the injected writer the
tests isolate on, which the file header records. The eight hold-out misses were traced to their
fields: four action types the samples never showed (`reset_cadence`, `schedule_sms_reminder`,
`branch_on_intent`, `start_esign_flow`), three call-to-action types (`reschedule`,
`intent_capture`, `review_renewal_details`), the long branch of the new-prospect row, and a send
day and hour per stage where the program has one hour per channel. D81 to D88 are the plan to
raise every dimension to 9.

**D81. The hold-out becomes training data and a new frozen set carries the honest number
(proposed 2026-09-10).** Question: D9 forbids fitting to `holdout_12.jsonl`, the catalog has two
rows because `sample.jsonl` has two records, and the hold-out reads 4 of 12 with every miss a rule
the samples never showed. Options: (a) playbook step 103: declare the hold-out training data, write
a new frozen set first, from the personas and stages in `problem_statement.txt`, labeled before any
rule lands and by an agent that has not read `src`, and report that set as the honest number; (b)
keep D9 and 4 of 12; (c) write more labeled data without reading the hold-out and fit to that.
Recommendation: (a), the step the playbook wrote for this case. Scopes: `synthetic_v2.jsonl`, an
amendment to D9, the numbers in the README and DESIGN.md section 9, `BaselineNumbersTests`.
Evidence: the score fact above and the hold-out trace of 2026-09-10. Assumptions: none. Check: the
frozen set is committed before the first D82 rule, and every report labels which set is which.
Order: first, alone.

**D82. The rules the hold-out shows, and where they live (proposed 2026-09-10).** Question: eight
misses need seven catalog rows (`prospect/no_show`, `prospect/cancelled_manager`,
`resident/renewal_window`, `resident/renewal_undecided`, `resident/welcome`,
`resident/loyalty_engage`, `resident/renewal_details_requested`), the long branch of
`prospect/new`, four action types, three call-to-action rows, and a send slot per persona and
stage with a day offset and an hour, where today one hour per channel is the whole rule. Options:
(a) rows in code as today, each with its record as evidence; (b) a JSON rule file loaded by a
`--rules` flag and validated per row through the existing `Create` gate; (c) both, the compiled
table as the default and the file as an override. Recommendation: (c): a cadence rename should
not need a build, and the program stays runnable with no file. Scopes: `Decisions/` (catalog,
action types, planner, scheduler and a new slot table), `Composition/CallToActionCatalog.cs`, the
input members a slot rule reads (`lease_end_date`, `move_in_date`), the `--rules` flag in
`CliRunner.cs` after D86 merges, DESIGN.md sections 3 and 7. Two agents in disjoint files: one for
rows, types and call-to-action rows, one for the slot table. Evidence: the hold-out trace.
Assumptions: a rule fitted to one record per stage is fitted, not learned; D81 is what measures
it. Check: every measured check on the hold-out passes, `synthetic_12.jsonl` stays 12 of 13, and
`synthetic_v2.jsonl` is reported at whatever it reads.

**D83. A generic-row answer is queued for review (proposed 2026-09-10).** Question: when no row
covers a persona and stage the generic row emits a confident action and only the diagnostics say
so. Options: (a) the record also gets a review-queue row naming the missing pair, and the message
still goes out; (b) `no_op` with a reason and no message; (c) keep. Recommendation: (a): the queue
is the work list an operator already reads, and the generic welcome is safe to send while the
action is reviewed. Scopes: `Orchestration/` (the result carries a queue reason for a generic
source), `ReviewQueueEntry`, the fold in `CliRunner.cs`, OPERATIONS.md. Same agent as D84, since
both edit `LeasingMessageAgent.cs`; rebased on D86. Evidence: eight of twelve hold-out records
took the generic row. Assumptions: none. Check: `synthetic_02_unseen_persona_and_stage` produces a
queue row; the hold-out queue is empty after D82.

**D84. One validation per composed record (proposed 2026-09-10).** Question: the same validator
instance runs on a draft inside the compose-validate loop and again at step 5. Options: (a) the
loop returns the draft paired with its validation result, in a type only the loop builds, and
step 5 reads the result it was handed and validates only a refused draft; (b) move the loop into
the orchestrator; (c) keep. Recommendation: (a). Scopes: `ComposeOutcome.Composed`,
`ValidatingMessageComposer.cs`, step 5 of `LeasingMessageAgent.cs`, the tests that count
validator calls. Evidence: the score fact; the D57 rule that two components computing one
predicate is a finding. Assumptions: none. Check: a counting validator sees one call per composed
record and one per refused draft; every tally unmoved.

**D85. A scorecard that cannot carry stale tallies (proposed 2026-09-10).** Question: the tallies
and p95 are field initializers on a record, so a `with` copy that replaces the rows carries the
old numbers, and AGENTS.md warns about it instead of the type preventing it. Options: (a) a sealed
class with one constructor and no `with`, the append method building a new instance as it already
does; (b) tallies computed on read; (c) keep. Recommendation: (a). Scopes:
`Evaluation/Scorecard.cs`, `ScorecardFormatter.cs`, its tests, the AGENTS.md gotcha line.
Evidence: the gotcha in AGENTS.md. Assumptions: none. Check: `with` on the type does not compile;
every scorecard golden unchanged.

**D86. Bounded memory in the batch (proposed 2026-09-10).** Question: every output, diagnostics
and queue row stays resident until the batch ends and is serialized once, O(n) in records.
Options: (a) an ordered fold that writes each row in input order as it completes, behind a
reorder window of `MaxConcurrentRecords`, the array written incrementally, the small scorecard
rows staying resident; (b) keep; (c) JSONL output. Recommendation: (a). Scopes: the loop and fold
in `CliRunner.cs`, `JsonArrayRecordWriter.cs` (begin, row, end), `RecordRun.cs`. Evidence: the
score fact. Assumptions: none. Check, per the Bounded Cost rule: wall time and peak working set at
12, 120, 1,200 and 12,000 records pasted into DESIGN.md, wall time linear, working set flat past
120, and the three output files byte-identical to today on the three sets.

**D87. Comments state the rule, not the decision number (proposed 2026-09-10).** Question: comments
in `src` and `tests` cite D and S numbers that resolve only in this archive, so no file reads
without it, and every trim of the archive is a repo-wide grep. Options: (a) every comment states
the rule and its why in place, no D or S number in code, at most eight lines per comment, the
archive keeps the history, and the citation check drops `src` and `tests` from its scan; (b)
keep. Recommendation: (a). Scopes: every file under `src` and `tests`,
`check-instruction-files.ps1`, the AGENTS.md workflow line, then `sync-agent-rules.ps1`. Order:
last, after every other stream merges; every other stream writes its new comments in this form.
Evidence: the Sprint 8 and D68 facts recording the two renumbering sweeps. Assumptions: none.
Check: a grep for a D or S number under `src` and `tests` returns nothing; build, tests and the
instruction check unchanged.

**D88. A mutation score beside the coverage gate (proposed 2026-09-10).** Question: coverage at
100 percent proves every line ran, not that a test would notice a changed line, and the glossary
already names mutation testing as the number that replaces it. Options: (a) Stryker.NET as a
tool-manifest tool in `test.ps1` and CI, the threshold pinned at the first measured score and
raised as it rises, coverage kept; (b) coverage only. Recommendation: (a). The agent confirms the
current package version and its .NET 10 support from the Stryker documentation before adding it;
neither was checked when this was written. Scopes: `test.ps1`, `.github/workflows/test.yml`, a
tool manifest, DESIGN.md section 9. Order: set up in parallel, the threshold pinned after D87
merges, since every stream moves the score. Evidence: the glossary entries for coverage and
mutation testing. Assumptions: none. Check: a planted mutant, the planner threshold flipped from
at most to under, is reported killed, and the score is in `test-output.txt`.

**D81 to D88 taken (2026-09-10).** The owner took every recommendation on 2026-09-10 after PR #32
merged: D81 (a), D82 (c), D83 (a), D84 (a), D85 (a), D86 (a), D87 (a), D88 (a). The log's open
line said taking D81 sends the project to Phase 2; step 9, the frozen set, is in Phase 0, so the
project returns to Phase 0 and each later phase's check is run again before the next phase's work
merges (step 102). Work is written in parallel, one worktree per decision, and merged into the
sprint branch in phase order (step 103): D81 (Phase 0); D85 and D88 (Phase 2, the harness); D82,
D84 and D83 (Phase 3, the core, D82 only after D81 has merged); D86 (Phase 6); D87 (Phase 8). D83
moves after D86 rather than beside D84, since both D83 and D86 edit the fold in `CliRunner.cs`.
D88 lands its tool and a first measured score in Phase 2 and pins the threshold after D87. One PR
per sprint (AGENTS.md) rather than per decision (step 103): each decision is its own merge commit
on the sprint branch, so it is reviewed as a unit inside the one PR.

**Run and debug fact, Sprint 15 wave 1 (2026-09-10).** Wave 1 launched five agents in worktrees
while the phase read 0, so four of them edit `src` and `tests` on unmerged branches, against the
AGENTS.md rule that nothing under `src` or `tests` is edited while the phase is 0 or 1. The D85
agent named the conflict. The exception is deliberate: the owner asked for every agent at once,
D84 to D88 are later-phase root causes whose decisions are taken, and the rule's guarantee, no code
before intake, is kept where it lands: no branch that touches `src` or `tests` merges into the
sprint branch until Phase 0's check passes after D81. The D85 review found the new constructor
kept the caller's list by reference, so a caller that edited it afterward reported rows that
disagreed with the tallies counted at construction, the bug D85 exists to close; fixed test first
on the D85 branch, the failing test reporting two rows for one counted, then 86 and 565 tests
passing at 100 percent coverage. The same agent found that nothing in the repository turns
warnings into errors: no `TreatWarningsAsErrors` in `Directory.Build.props` or any project, and no
`-warnaserror` in CI, so the rule holds only when a build passes the flag by hand (D89).

**D89. Warnings as errors in the build (proposed 2026-09-10).** Question: the code rules require
the strictest checker with warnings as errors, and nothing in the build enforces it; a
`dotnet build -warnaserror` of the D85 branch was clean, so today the rule holds by chance.
Options: (a) `TreatWarningsAsErrors` true in `Directory.Build.props`, so every local build, test
run and CI run enforces it; (b) `-warnaserror` in the CI step only; (c) keep. Recommendation: (a),
since a rule that must hold with no exceptions belongs in tooling, and (b) lets a local build pass
that CI then fails. Scopes: `Directory.Build.props`, and any warning the flag surfaces on `dev`.
Evidence: the wave 1 fact above. Assumptions: none. Check: a planted unused variable fails
`.\test.ps1` locally and in CI. Order: Phase 1, the environment, so it merges right after D81.

**Run and debug fact, D81 merged (2026-09-10).** `synthetic_v2.jsonl` landed as 29 labeled
records and one malformed line, line 15, run with `--now 2026-10-24T22:00:00Z`. A separate
validation, not the author's, parsed 29 lines with unique task ids, found exactly one malformed
line, 26 sent and 3 suppressed records, every suppressed record in the channel `none` shape with
`no_op`, and every `send_at` an ISO-8601 time with an offset. The author read only
`problem_statement.txt` and `sample.jsonl`; AGENTS.md was loaded into its context automatically,
and the only internal it names that a label uses, the suppression shape, was also in its brief.
The labels disagree with this program's own assumptions on purpose: a 60-day short horizon
against A7's 45, a zone inferred from the city against A6's UTC, follow-up gaps of 2, 3 and 7
days, and every send on a Sunday. Those misses measure disagreement with a stated assumption
rather than a defect, and D82 is fitted to the hold-out and never to this set. D9 is amended by
D81 from this date: `holdout_12.jsonl` is training data, and every report names which set is
which. Phase 0's check passed with A24 added: the assumptions log has no blank evidence cell.

**Run and debug fact, Phases 1 and 2 re-run, D85 and D84 merged (2026-09-10).** Phase 1's check
passed from a fresh clone of `sprint-15-architecture` at `06149a7` taken from origin: `.\test.ps1`
exited 0, 86 and 564 tests, 100 percent line, branch and method coverage in both projects. D85
merged first, since Phase 2 is the harness, and Phase 2's check, the scorer proofs, passed inside
the gated suite. D88 is also placed in Phase 2 but touches no file under `src` or `tests` and
pins no threshold until after D87, so its first half merges when its measurement finishes rather
than holding the Phase 3 merges behind a mutation run; this is a reorder inside step 103's
phase order, named here. D84 merged next as the first Phase 3 decision. Its design, which goes
past the brief: the loop's clean exits attach a verdict object that only the library can
construct, and the final gate reuses it only for the same validator instance, an equal draft and
equal constraints, validating everything else, a refused draft, another composer's output, and a
loop outcome a wrapper copied with a new message, since a `with` copy of the public outcome
record carries the old verdict. The gate compares the draft before the send time is set, and a
new test pins that no safety check reads the send time. The agent also found that the test
factory built two validator instances where the runner shares one; it now shares one by default.
Under production wiring each composed record is validated once, down from twice; a refused draft
is still validated four times, three in the loop and once at the gate.

**Run and debug fact, D86 first change (2026-09-10).** The output side streams: a window of at
most four runs ahead of an ordered fold writes each record's output, diagnostics and queue rows
as soon as every earlier record has finished, and the array writer writes begin, one row, end,
byte-identical to serializing the list. On the Release build, median of three runs, peak working
set without `--eval-report` went from 41.8, 44.1, 70.1 and 187.4 MB to 40.8, 42.8, 62.0 and
92.3 MB at 12, 120, 1,200 and 12,000 records, and wall time was unchanged within noise. The three
documented sets match the baseline byte for byte once latency figures are masked, with exit
codes 0, 0 and 2. D86's check, a working set flat past 120 records, is not met: the reader returns
every parsed line as one list and the model-call budget reads every record before the composer is
built. Four behavior changes come with it: a cancelled run leaves `--output` holding the rows
folded so far with no closing bracket where it was empty; stderr failure lines are written as
soon as every earlier record has finished, still in input order; the scorecard's batch latency
now includes the row writes; and a slow record at the head of the window holds back later starts,
unmeasured with a model in the path. The error writer is wrapped as synchronized, since the fold
writes to it while records log to it.

**D86 addendum (2026-09-10).** The decision's check cannot be met inside the scope it listed,
because the input is held whole before the first record runs. The scope extends, under the
decision already taken, to `JsonlRecordReader` (a lazy per-line read with the same line numbers
and failure text), `ModelCallBudget` (the strictest budget found by a first streaming pass that
keeps only the running minimum, taken only for the model composer with no override, so the
template path reads the file once), `Evaluator` (one public per-record scoring method that the
batch path also calls, replacing a one-row scorecard built per record) and the three lines of
`docs/OPERATIONS.md` the first change made wrong. The check is unchanged: without
`--eval-report`, peak working set at 12,000 records within 10 percent of 120.

**Run and debug fact, D88 first half merged (2026-09-10).** Stryker.NET 4.16.0 is a local tool
pinned in `.config/dotnet-tools.json`, confirmed against its NuGet index and the Stryker
documentation, which states it requires the .NET 10 runtime or newer. It mutates one project
under test per run, so there are two configurations, `stryker-config.json` for the library and
`stryker-config.cli.json` for the command line, each with `thresholds.break` at 0, the documented
default, until the pin after D87. `mutation.ps1` runs both and tees to `mutation-output.txt`.
Stryker's own MSBuild discovery chose Visual Studio 2022's MSBuild 17.14, which resolved the
.NET 9 SDK and failed to analyze every `net10.0` project, so the script passes the MSBuild of the
SDK that `dotnet --version` resolves; a CI image with Visual Studio needs the same. Measured on
`2772990`, two runs with the same scores: the library 78.23 percent (721 killed, 202 survived, 5
timeouts, 238 compile errors, 113 ignored of 1279), the command line 87.44 percent (194 killed,
24 survived, 4 uncovered, 1 timeout, 68 compile errors, 40 ignored of 331), 4.4 minutes on 16
workers. The planner mutant `days < ShortHorizonThresholdDays` is killed by the boundary test at
45 days. Two methods, `IngestNotes.Collect` and `OpenAiCompletionClient.CompleteAsync`, are
unmeasured: a CS0165 compile error in each dropped every mutant there. 69 of the library's 202
survivors are string mutants in the stop-word lists of `LanguageDetector`. The ten survivors the
agent ranked first are each a boundary or branch no test pins: the scheduler's roll to the next
day when the floor equals the send hour, `ActionCatalog.Create` refusing an unknown long-horizon
type on the generic and the persona rows, the template's "at property" phrase and empty-interest
sentence, the judge's body `NotMeasured` rule, the fact-coverage half-match boundary, a value
flag passed last with no value, the deterministic-check set, an empty violations list in the
prompt, and `--log-file` appending across runs. They are the input to the threshold pin.

**Run and debug fact, D82 send slots merged (2026-09-10).** A send-slot table keyed on persona,
lifecycle stage and channel gives each row a count of local days after the floor's day and a
local time; a key with no row keeps the channel's hour on the floor day, and the schedule
diagnostics name which rule answered as `slot_row` or `channel_default`. Nine rows, one per
hold-out record whose label is not the channel's hour, each fitted to that one record. On the
hold-out, before and after on the agent's branch: send day 7 of 11 to 11 of 11, send hour 5 of
11 to 11 of 11, overall 4 of 12 to 7 of 12; `synthetic_12.jsonl` unchanged at 12 of 13. A day
offset is counted in local days, so five days across a spring-forward change still lands at
09:00 local. No rule reads `missed_tour_time`, `move_in_date` or `lease_end_date`: each has a
floor-relative rule that fits equally and needs no new member, so the ingest notes still list
them as unknown, and D82's scope line naming two of them is not used. The scope-out "no minutes"
is obsolete in DESIGN.md section 8, A5 and section 3's send rows; the quiet-hours scope-out in
`docs/CODE_REVIEW.md` stands. The evaluator's hour check compares the hour only, so a send one
minute or fifty-nine minutes off the label scores the same. The file override D82 option (c)
calls for is not built: the table is compiled, and the loader, with row validation, lands with
the `--rules` flag after D86.

**Open question, the key for two send slots (2026-09-10).** Two rules reproduce the hold-out's
09:05 for a prospect at new on email and 09:20 for a prospect at open on sms: a row keyed on the
channel, and a row keyed on the record having no `last_interaction`. Every other record with a
row differs from its pair on the channel as well, so the hold-out cannot tell the two apart.
The channel rule is in the code, because the channel is already the scheduler's key and whether
a record carries a last interaction says nothing about the time of day. Under it three records
of `synthetic_12.jsonl`, each a prospect at open on sms with a last interaction, now send at
09:20 where their labels say 09:00. Those labels were written from A5, the rule this change
replaces, so they restate the old assumption rather than observe a send, and they are not
evidence either way. What would settle it: a labeled record that is a prospect at open on sms
with a last interaction, or at new on email with one. Until one exists the choice is a stated
assumption and not a learned rule, and no check measures it.

**Run and debug fact, D82 rows merged (2026-09-10).** Four action types join `ActionTypes`
(`reset_cadence`, `schedule_sms_reminder`, `branch_on_intent`, `start_esign_flow`), and the
catalog gains the long branch of prospect at new and a row each for prospect at no show and at
cancelled by the manager, and resident at renewal window, renewal undecided, welcome, loyalty
engage and renewal details requested. Every one of those hold-out records states no move date,
so each row states only its long branch. The call-to-action table maps `reschedule_tour` to
`reschedule` and `reply_intent` to `intent_capture`, and a record with no `primary_cta` now takes
its persona and stage's default where one exists (`schedule_tour` for prospect at new,
`review_renewal_details` for resident at renewal details requested) before the generic `reply`;
a stated `primary_cta` still wins. On the agent's branch the hold-out's action went from 7 of 12
to 12 of 12 and call to action from 7 of 11 to 11 of 11, and `synthetic_12.jsonl` held at 12 of 13.
Three things no check measures: the action check compares the type only, so the follow-up
values 2 and 5, the reminder's `in_days` and the reply mapping are unscored, and the last two
are not emitted because `NextAction` has no member for them; `prospect_spanish_locale` is
labeled a follow-up in 2 days and gets 3 from sample 2's row, and five fields separate the two
records, so no rule was written; and the labels' resident links and subjects are not built. The
long-branch name `prospect_welcome_long_horizon` is fitted where more than one field separates
that record from sample 1; the type is the same on both branches. The generic row is the same
for every persona, so a resident whose move date falls inside the short horizon, at a stage whose
row states only the long branch, is answered with the prospect welcome cadence; D83 queues it for
review. `-warnaserror` proves nothing on an up-to-date tree, since nothing recompiles: the
warnings check is `dotnet build -warnaserror --no-incremental`. A warnings build that ran beside
the mutation run on the same folders reported one error that a clean rebuild after stopping it
did not reproduce: two builds of one tree at once are not a measurement.

**Run and debug fact, Phase 3 re-run after D82 (2026-09-10).** A confirming `mutation.ps1` run
was started on the sprint tip in the main checkout, and the D82 slot merge's build, suite and
scoring were started in the same checkout while it ran; its warnings build reported one error
that a clean rebuild did not reproduce. The mutation task was stopped. Two later process checks
started from a bash chain reported Stryker still running, and the process ids they named were
already gone when stopped: the check's own pattern was on the command line of the bash chain
that launched it, so it matched its parent shell, and a check that excludes only itself and its
own children cannot tell a shell from a Stryker process. Whether any Stryker process outlived the
task stop is not established. A check that matched on the process name and on the Stryker
command line alone, run directly, found none, and every number below was taken again after it on
a tree with no Stryker process: they match the earlier run exactly. Both D82 halves merged; the
one conflict, the hold-out row of `BaselineNumbersTests`, was resolved by pinning the numbers the
merged code produced on its own hold-out run. After both: 86 and 616 tests, 100 percent line,
branch and method coverage, a clean `dotnet build -warnaserror --no-incremental`. Phase 3's
check passed: `synthetic_12.jsonl`, the step 9 set of record, reads 12 of 13 with its malformed
line an error row, unchanged, under the template composer with no model; and every record on all
four sets carries an action source, a schedule floor and source, a composer and a required-state
map, or a suppression reason. The hold-out reads 12 of 12, fitted since D81, so it shows the
rules reproduce their evidence and nothing about generalizing. Every review queue is empty.

**Run and debug fact, the honest number after D82 (2026-09-10).** `synthetic_v2.jsonl` reads 13
of 30 on the template composer: channel 25 of 29, send day 24 of 26, send hour 19 of 26, action
17 of 29, call-to-action type 23 of 24, payload 24 of 25, opt-out, language, safety and
personalization full, and the malformed line 15 an error row. Its sixteen failing records:
eight are stages no row covers (prospect at toured, applied, approved and lost; resident at
active, renewal and notice given; prospect at renewal), answered by the generic row, which D83
queues for review; three are the author's judgment against a stated assumption, an empty
preference list and a consent only for a channel not preferred both labeled as sent by email
where A1 and A3 suppress, and an unknown zone labeled in Central time where A6 uses UTC; two
are the voice hour, labeled 10:00 where A5 says 09:00, and a past move date labeled a new
cadence where A7 gives the generic follow-up. The last three are the prospect at new on email
slot: each sends at 09:05 where its label says 10:00. That row is the one the open question of
2026-09-10 names, and the frozen set disagrees with the channel key there. Choosing the other
key because of it would fit a rule to the frozen set, which D81 forbids, so the disagreement is
recorded and the choice stands until a labeled training record settles it. The scorecard is
committed as `docs/scorecards/synthetic_v2_template.txt`.

**Run and debug fact, D86 merged with its addendum (2026-09-10).** The input streams too: the
reader yields one line at a time, the runner runs the batch straight off it, and a line that
did not parse waits for every earlier record to be folded so stderr keeps input order. For the
model composer with no override, a first pass through its own reader keeps only the strictest
`p95_latency_ms` and rewinds the file; the template path reads the file once, which rests on the
code and on no test that counts reads. One per-record scoring method on the evaluator serves the
batch and the fold. What stays resident without `--eval-report` is the window of four runs and
the failure text of each unparsed line; with it, one score row per record; with `--judge`, one
scored run per record. Measured on the Release build, median of three runs, peak working set
without `--eval-report`: 40.9, 42.9, 57.9 and 62.0 MB at 12, 120, 1,200 and 12,000 records,
against 41.8, 44.1, 70.1 and 187.4 MB before D86. D86's check, within 10 percent of 120 at 12,000,
is not met: 62.0 against 42.9 is 45 percent. With the managed heap capped at 16 MB the same build
runs 120 records at 41.0 MB, 12,000 at 44.1 MB, within 8 percent, and 120,000 at 55.4 MB, where
the code before D86 ran out of memory at 12,000; so the program no longer holds records, the
default growth is the collector sizing its heap, and about 11 MB of the growth from 12,000 to
120,000 is outside the managed heap and not attributed without a profiler. After the merge all
four sets match the D82-merged runs file for file, output and queue raw, diagnostics without
`latency_ms`, reports with latency masked; 92 and 630 tests pass at 100 percent coverage.
Phase 6's check passed: the documented command wrote the output, diagnostics and scorecard on
all four sets and exited 0, 0, 2 and 2. Three behavior changes: a usage error such as an unknown
composer now exits before any parse-failure line is written; the batch latency now includes
reading and parsing the input and writing the rows; and the first pass rewinds the input, so a
path that cannot seek, such as a pipe, would throw on the model path with no override, which no
documented input is.

**D86 check amendment (proposed 2026-09-10).** Question: the check measures peak working set at
the runtime's default settings, which moves with the garbage collector's heap sizing as well as
with what the code keeps, and the measurement above separates the two. Options: (a) the check
becomes a run with the managed heap capped, 12,000 records within 10 percent of 120, which the
code now meets, plus the 120,000-record run completing under the same cap; (b) keep the check
and meet it with a collector setting in the command line's runtime configuration, a product
change made to satisfy a measure; (c) keep the check and leave D86 open. Recommendation: (a),
since what the decision set out to bound is what the program holds per record, and (b) would
tune the runtime to pass a number. Scopes: D86's check line and the design doc's benchmark only.
Evidence: the capped and uncapped tables above. Assumptions: none. Not taken: a check changed
after its result is the owner's call.

**Run and debug fact, D83 merged (2026-09-10).** A record whose next action came from the
generic row now also gets a review-queue row naming what had no row: the persona and stage for
`generic_row_no_match`, and the branch too for `generic_row_no_branch`, with the action given.
The message still goes out and `--output`, `next_action`, the diagnostics and the exit code are
unchanged. Every queue row carries `reasons`, and a record refused on safety that the generic
row also answered is one row with both; `violations` and `draft` keep their place first and are
null on a row not refused on safety, so a safety row reads as before apart from the two members
appended after them. The decision's scope named a queue reason carried on the result; the queue
entry reads the existing `action_plan` instead, so one fact is not stored twice. A record whose
composition failed is queued when the generic row chose its action, which the decision did not
say. Queue rows before and after: `sample.jsonl` 0 and 0, `holdout_12.jsonl` 0 and 0,
`synthetic_12.jsonl` 0 and 3, `synthetic_v2.jsonl` 0 and 11; output, diagnostics without
`latency_ms` and reports with latency masked identical on all four. The decision's check passed:
`synthetic_02_unseen_persona_and_stage` is queued with `generic_row_no_match`, and the hold-out's
queue is empty.

**Run and debug fact, a log line that carried record text (2026-09-10).** The generic-row log
line has named `persona` and `stage` since it was written, and D83 raised it from Information to
Warning with a comment calling those values catalog keys. They are the record's own free text,
and on a missed match they are by definition not catalog keys, so the line broke the rule that a
log carries no value a record authored; the D83 brief asked for the line to carry them while
also saying it carried no record text, so the fault began in the brief. Fixed test first on the
D83 branch: the test gave the record a marker persona and stage and asserted neither reached the
line, and it failed with `persona=persona-marker-7f3a` in the message; the line now names the
source and the branch and says the review queue names the pair. A first red on that test came
from the test's own assumed branch rather than from the leak, and was corrected before the fix
was written, since a red for the wrong reason proves nothing. The operations guide's logging
table still listed the line under Information and now lists it under Warning.

**Run and debug fact, D82 rules-file loader merged (2026-09-10).** A rules file in JSON holds
the action catalog, its generic row and its rows, and the send-slot rows, and replaces the
compiled ones whole rather than merging with them; the call-to-action table stays compiled. The
loader reads each row on its own and reports every bad row in one failure, by its 1-based place
in its array and by its key: a blank persona or stage, a duplicate key, an unknown action type, a
value that is not positive, a slot whose channel is absent or not sms, email or voice, whose day
offset is absent, negative or above 365, or whose local time is absent or not 24-hour `HH:mm`.
A member the format does not define, or one stated twice, is refused rather than skipped, so a
misspelled member cannot drop a rule silently; a duplicated member fails the whole file, since
the parse into rows applies that check first. Invalid JSON fails with the exception type and a
position and no text from the file. `ActionCatalog.Create` now names every bad row where it named
the first, and exposes its generic row and rows for the round-trip test; `SendSlotTable` is a
value the scheduler takes, the compiled rows its default. The compiled rules written as a file
and read back give equal answers for every compiled key on both branches and every channel.
After the merge all four sets match the D83 runs file for file, queues included. Two findings:
the 365-day cap on a slot's day offset is the agent's judgment, a guard against date overflow in
the scheduler with no data behind the number (A25); and the position a parse failure reports
comes from the shared exception formatter, whose line number is zero-based, harmless for a JSONL
line and misleading in a multi-line rules file.

**Run and debug fact, D82 `--rules` flag merged (2026-09-10).** `--rules <file.json>` loads a
rules file after `--input` opens and before the composer is built or any batch output opens, so
a refused file costs no record, no model call and no partial output; a CLI test proves the order
by pairing a bad rules file with `--composer openai` and no key, and getting the rules failure.
The loaded catalog goes to the planner and the slot table to the scheduler; with no flag the
compiled rules apply. A path that will not open prints `Could not open --rules '<path>'`, a file
the loader refuses prints `Could not load --rules '<path>':` and one line per bad row, both
exit 1, and `--rules` with `--replay` is refused, since a replay runs no planner. The log states
the catalog and slot row counts and nothing else from the file. `--log-file` still opens before
the rules load, as it must for the logger to exist, so a refusal also lands there. A rules file
holding exactly the compiled rules gave identical output, diagnostics, queue and report on all
four sets; after the merge, 97 and 660 tests pass at 100 percent and all four sets match the
loader-merge runs file for file. One optional finding, not fixed: the load helper returns a
result holding a nullable value for "no file given", where AGENTS.md reserves `Option` for an
expected absence; it is correct as written. A report compare that masks only figures ending in
"ms" shows false differences, because the per-record latency column holds bare numbers.

**Run and debug fact, D87 in Common, Domain and Ingest (2026-09-10).** 21 comments in 19 files
were rewritten to state the rule and its reason in place and cite no decision number, each at
most eight lines. The agent's comment-stripping comparison found 35 files identical with comments
removed, and a separate check of the diff found every added or removed line a comment line, no
file outside the three folders, and no `D` or `S` number left in them. Three archive paragraphs
gave a rule and no reason a comment could state: the one-record shape of a next action keeps
its existing "the way the oracle spells them", the resolution's note that it is a diagnostics
value was dropped as already said, and the consent comments state that only an explicit true
opts a channel in without a separate why.

**Run and debug fact, D87 in Decisions, Composition and the library tests (2026-09-10).** 48
comments in 23 files of `src/Agent/Decisions` and `src/Agent/Composition`, and 142 comment
blocks in 31 files of `tests/Agent.Tests`, were rewritten to state the rule and its reason and
cite no decision number; each agent's comment-stripping comparison, with negative controls for
`//` inside every kind of string, found every file identical with comments removed, and a
separate check of each diff found only comment lines, inside its folders. No string literal,
identifier, test name or `InlineData` value in those folders carried a number. Four comments were
stale and corrected as they were rewritten: the model client's budget comment said the loop
composes again after a failed call, where since D34 it retries only a safety rejection; two
comments named a retry count on `CompositionNotes` that moved to `ComposeOutcome`; and one said
the composer catches a missing completion choice into a result, where it returns a failed
outcome. One library comment block stays at nine lines, since it cites no number.

**Finding, a completed call counted as not completed (2026-09-10).** The model client throws
`InvalidOperationException` when a completion comes back with an empty body, after the call
completed and its usage block was read, and the model composer's general catch records that
case as no completed call with zero tokens, so the run's token count undercounts what the vendor
billed. The comment there says every exception it catches happens before or instead of a
completion, which this case contradicts. Untested and found by reading during the D87 sweep; no
change was made, and it needs its own decision before a fix.

**Run and debug fact, D87 in Safety, Orchestration and Evaluation, and three stale comments
(2026-09-10).** About 75 comments in 32 files were rewritten to cite no decision number, and the
agent's comment-stripping comparison, which copies the validator's regular expressions and the
judge's raw-string rubric through as literals, found all 39 files identical with comments
removed; a separate check of the diff found only comment lines inside the three folders. Blocks
that could not fit eight lines were split onto the members they describe: the diagnostics header
onto each parameter, the verdict table onto each enum member, the scorecard header onto each
property, and the validator's switch-off rules onto the three check methods, where each now says
why that check may or may not be switched off. The allow-list comment lost its example sentences
and keeps each span and why it is exempt; the archive gives no reason for the individual benign
spans, so the comment states them as legitimate uses of a term and names the test that pins the
span that matches nothing. After the merge no file under `src/Agent` or `tests/Agent.Tests`
cites a decision number, and 97 and 660 tests pass at 100 percent. Three comments were stale and
were corrected on the sprint branch, comment lines only: a template test said the sms option
text comes from the catalog, where it comes from the record's language set keyed by the
call-to-action type; and the required-state map and verdict said `renewal_offer_loaded` has no
check because nothing is fitted to an evaluation set, which stopped being the reason when D81
made the hold-out training data, and the map also said the records asserting it carry an offer
id, where of the three only two do. The reason now given is the true one: the records disagree
and no check scores a required state, so nothing says what earns it.

**Run and debug fact, D87 closed: the command line, its tests, and the check that holds it
(2026-09-10).** 68 comments in six files of `src/Agent.Cli` and `tests/Agent.Cli.Tests` were
rewritten to cite no decision number, and the agent's comparison, with a negative control that a
changed string literal must show, found all 20 files identical with comments removed; a separate
check of the diff found only comment lines inside the two folders, and no printed message or test
name carried a number. One reference was stale and dropped: the record catch block named
`Parallel.ForEachAsync`, which the windowed batch loop no longer calls. Two reasons were inferred
from a paragraph's evidence rather than read from a stated reason, the reference-time flag's and
the model-call budget override's, and say so only by stating what the evidence showed. The input
helper's comment named two paths a run reads where `--rules` is a third, and was corrected. With
every folder swept, `check-instruction-files.ps1` gains a fifth rule: any `D` or `S` number in a
tracked file under `src` or `tests`, comments, strings and names alike, fails the check with its
file and line, and the citation scan that resolves numbers against the archive reads only the
docs, the README and AGENTS.md. AGENTS.md states the rule once in its workflow section and names
it among the check's rules. The check is CI's first step, so the rule now holds by tooling rather
than by a sweep that has to be repeated.

**Run and debug fact, D88 pinned (2026-09-10).** The pin waited for the code streams to stop
moving the score; once D82 to D86 had merged and D87 was a comment sweep, which a mutation run
cannot see, the score was measured on a fresh clone of the pushed sprint branch at `fba73d7`,
isolated from the main checkout after an earlier run there shared build folders with a merge.
`mutation.ps1` exited 0 in 10 minutes: the library 82.35 percent, 938 killed or timed out of
1,139 with 201 survived, 282 compile errors and 143 ignored; the command line 87.94 percent, 248
of 282 with 30 survived, 4 uncovered, 71 compile errors and 65 ignored. The library rose from the
78.23 percent measured before the sprint's tests landed. Each configuration's `thresholds.break`
is its score rounded down, 82 and 87, so the gate starts where the code is and fails only on a
drop. A `mutation` job in `.github/workflows/test.yml` runs `mutation.ps1` after the gated suite
and keeps its output; it gates a merge only once branch protection requires it, which is the
owner's setting. `test.ps1` does not run it: D88 option (a) named both, and a ten-minute mutation
run inside every test-first cycle would make the cycle the bottleneck, so the local gate stays
the coverage suite and the mutation gate is CI's. That departure from the option's wording is
named here rather than taken silently. The pinned commit's own run and a negative control with
the command line's threshold raised to 99 are the next run and debug fact.

**Run and debug fact, an invalid pin caught by running the gate (2026-09-10).** The D88 pin
raised `thresholds.break` to 82 and 87 and left `low` at 60, and Stryker.NET refuses a
configuration where low is under break: the pinned commit's own run exited 1 within a second in
both projects with "Threshold low must be more than or equal to threshold break", so the pushed
pin broke `mutation.ps1` and would have failed the CI job on every run. The negative control,
break raised to 99, failed with the same message, so it showed nothing about the score and is
redone. Fixed by setting `low` equal to `break` and `high` to 90 in both configurations, so high,
low and break are in the order Stryker requires; high and low only color the report, and break
is the gate. The pin was written without running the thing it configures, which is the failure
Verify First names; the run that caught it was already planned as the pin's proof.

**Run and debug fact, the mutation gate's negative control (2026-09-10).** Redone after the pin
fix, in its own clone at `17da08e` with the command line's thresholds set to high 100, low 99
and break 99: Stryker ran for 97 seconds, scored 87.59 percent, logged "Final mutation score is
below threshold break", and exited 2. So the break threshold fails a run on the score itself,
which the first control, refused on the configuration, could not show. The same code scored
87.94 percent in the measurement run, because a mutant that times out counts as detected and how
many time out varies between runs; with the command line pinned at 87, that spread of 0.35 points
leaves a margin of 0.59, which is the input to whether a pin needs headroom below the measured
score rather than the score rounded down.

**Run and debug fact, the pinned mutation gate passes (2026-09-10).** `mutation.ps1` on `17da08e`,
the fixed pin, in the isolated measurement clone: exit 0 in 304 seconds, the library 82.44
percent against a break of 82 and the command line 87.59 percent against 87. Across the runs on
unchanged code the library read 82.35 and 82.44 and the command line 87.94, 87.59 and 87.59, so
the smallest margins over the pins are 0.35 and 0.59 points and the largest spread between runs is
0.09 and 0.35. Each margin exceeds its observed spread, so the pins stay at the score rounded
down, as D88 says. The spread comes from mutants that time out, which count as detected, so a
slower CI runner times out more of them and moves the score up rather than down. The margins are
thin; if the CI job fails on a score within one point of its pin with no code change, headroom
below the measured score is a decision for the owner, not a pin to lower in place.

**D89 and the D86 check amendment taken (2026-09-10).** The owner took D89 (a), warnings as
errors through `TreatWarningsAsErrors` in `Directory.Build.props`, so every local build, test run,
mutation build and CI run enforces it; and the D86 check amendment (a), so D86's check is a run
with the managed heap capped at 16 MB in which 12,000 records land within 10 percent of the
120-record peak working set and a 120,000-record run completes under the same cap. The owner
also asked for research on making the `mutation` job a required check on `dev` and for the best
course to be taken; that is D90. D89 is Phase 1 work, the environment, done on this sprint's
branch and PR because the phase it touches has passed again in this sprint and the change adds a
gate rather than a behavior; its check is a planted warning failing `.\test.ps1`.

**Run and debug fact, D89 in the build (2026-09-10).** `TreatWarningsAsErrors` is true in
`Directory.Build.props`. The check ran: a planted file holding an unused local variable made
`.	est.ps1` exit 1 with `error CS0219: The variable unused is assigned but its value is never
used`, and with the file removed `.	est.ps1` passed, 97 and 660 tests at 100 percent coverage, so
the code carries no warning under the setting. CI builds through the same properties file, so
the same setting holds there by construction; no failing commit was pushed to show it in CI.
Stryker compiles each mutant with the project settings, so a mutant that raises a warning now
fails to compile and leaves the score; the pinned gate is run again on this commit.

**D90. The mutation job as a required check on `dev` (proposed 2026-09-10).** Question: the CI
`mutation` job runs `mutation.ps1` after the gated suite on every pull request into `dev`, and
branch protection requires only `test`, so a fall in the mutation score does not block a merge.
Options: (a) add `mutation` to the checks `dev` requires; (b) leave it a report; (c) run it on a
schedule instead of per pull request. Recommendation: (a), once its runs on the CI runner pass in
a time a pull request can wait for, since a rule that must hold with no exceptions belongs in
tooling and a check that does not gate is a report. GitHub reports a required job skipped by a
condition as a success and keeps a workflow skipped by path or branch filters pending, which
blocks a merge; the job has no filters and is skipped only when `test` fails, which already
blocks. The risk is a failure on noise: the smallest margins over the pins are 0.35 and 0.59
points, and a slower runner times out more mutants, which count as detected and move the score
up, not down. Scopes: the `dev` branch protection setting only, through the GitHub API.
Evidence: the troubleshooting page for required status checks on docs.github.com, read
2026-09-10; the protection read back the same day, `test` required, not strict, admins not
enforced; and the job runs on PR #33. Assumptions: none. The owner asked for the research and for
the best course to be taken.

**Run and debug fact, D86 met under its amended check (2026-09-10).** The Release build of the
pinned code, in the isolated Phase 8 clone, run by the benchmark script the D86 agent wrote, with
`DOTNET_GCHeapHardLimit` at 16 MB on the child process, peak working set polled every 5 ms,
median of three runs, no `--eval-report`: 43.4 MB at 120 records, 45.5 MB at 12,000 and 55.2 MB
at 120,000, every run exiting 0. 12,000 is 4.8 percent above 120, inside the 10 percent the
amended check allows, and the 120,000-record run completes, so D86 is met. A first attempt
failed before measuring anything: passed through `powershell -File`, the list of sizes arrived
as one string the script could not read as integers; passed through `-Command` it ran.

**D90 taken, from the job run on the CI runner (2026-09-10).** The `mutation` job ran on the CI
runner for the first time on PR #33 at `fbc2f92` and passed in 23 minutes, 19.2 for the library
and 3.2 for the command line, against about five on a machine giving Stryker 16 workers; GitHub
documents the `windows-latest` runner as four CPUs in a public repository. The script resolved
the runner's SDK MSBuild, 10.0.401, and the scores were 82.35 and 87.59 percent, the same as every
local run on the same code. The run at `d9e5ee1` failed at the instruction check and GitHub
marked the job skipped, as a job after a failed need is, so a required job never leaves a pull
request waiting on a check that cannot start. The recommendation stood and was taken: `mutation`
joins `test` as a check `dev` requires, set through the branch protection API with both bound to
GitHub Actions, and read back after the change as both checks required, not strict, admins not
enforced, no required reviews and force pushes off. The workflow gains a concurrency group: a new
push to a pull request cancels the run for the head it replaced, since three runs were queued at
once on PR #33 and only the newest head decides a merge, and a push to `dev` or `main` is never
cancelled. The local rerun under D89 scored 82.35 and 87.59 percent with 282 and 71 compile
errors, unchanged, so warnings as errors turned no mutant into a compile error.

**Run and debug fact, the PR #33 review fixes (2026-09-10).** An Antigravity review of PR #33
confirmed four findings, all fixed on `sprint-15-architecture`, three agents working one file
each. `CliRunner.ModelCallBudgetFromInput` built an unread `firstPass` `StreamReader` and reset
`inputReader.BaseStream.Position` even when `evaluationOverride` was given, though
`ModelCallBudget.PerCallBudget` never enumerates the records in that case; it now returns the
override immediately, touching neither, matching the method's own comment. The review's
non-seekable-stream framing does not apply here: `OpenInputReader` always opens a real file
(`new StreamReader(path)`), always seekable, so no defensive `CanSeek` check was added, since
the coverage gate cannot honestly exercise a branch `--input` can never reach. `Scorecard.
ComputeLatencyP95Ms` is O(n log n), a sort; `Evaluator.Evaluate`'s comment said O(n), and now
states the true cost. `CliRunner.JudgeAndAppendAsync` called `Scorecard.AppendUnprocessed`
unconditionally, forcing a full sort-and-tally rebuild even with an empty list on a clean run; it
now returns the judged scorecard unchanged when there is nothing to append. `JsonArrayRecordWriter.
FlushAsync` never flushed `target`, so a row could sit in a buffered `StreamWriter`'s memory
instead of reaching disk; the existing test used an unbuffered `StringWriter` and missed it. A
new test, `WriteRowAsync_TargetIsABufferedStreamWriter_TheRowReachesTheFileBeforeDisposal`, opens
a real `FileStream`/`StreamWriter`, writes one row without disposing, and reads the same path
through a second handle; it failed on the unfixed code (`Assert.Contains` found nothing on disk)
and passed once `target.FlushAsync(cancellationToken)` was added. `dotnet test` on both projects:
Agent.Cli.Tests 97/97, Agent.Tests 661/661, both at 100 percent line, branch and method coverage.
