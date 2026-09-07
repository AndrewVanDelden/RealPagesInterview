# Retrospective and completion plan, 2026-09-06

Everything from the 2026-09-06 review session in one place: what was found, what was
decided, what was defined, what was written, what is still open, and the plan to finish
this project. Universal lessons live in the playbook and its appendices
(`~/.agent-rules/PROJECT_PLAYBOOK.md`); this file holds the project-specific record. The
per-record evidence is in the appendix at the end.

## 1. The state of the project, measured

Fresh run against the real 12-record hold-out (kept outside the repo):

| Measure | Value |
|---|---|
| Eval report | 2 of 12 pass |
| Honest count if `send_at` were scored to the day | 1 of 12 |
| Records with `send_at` in year 0001 | 7 |
| Records suppressed for a missing `primary_cta` that expected an email | 2 |
| Records where the planner had no usable input | 10 of 12 |
| Tests | 176, 100 percent line, branch, and method coverage |
| Source files | 66, of which 40 are under 15 lines |
| Interfaces | 11, of which 8 have one implementation and no test substitute |
| Log lines written by `--log-file` | 53, task id on every per-record line |

All commits are dated 2026-09-04, the interview day, inside a four-hour box. The eval
harness was Sprint 6 at 30 minutes and the hold-out runbook was Sprint 7 at 15 minutes,
both last.

## 2. Findings, in causal order

Each names the pillar (key: `~/.agent-rules/CODE_PILLARS.md`) and the fix item in section 6.

1. **The planner keys on the wrong fields** (correctness, LC). `Persona`, `LifecycleStage`,
   and `Language` are parsed and read by nothing. `NextActionPlanner.Plan` takes a move
   date and a last interaction; 10 of 12 records have neither. The oracle's action is a
   lookup on lifecycle stage and consent outcome. The horizon rule fit the two samples by
   coincidence. Fix 3.
2. **The orchestrator hands the planner three fields**, at `LeasingMessageAgent.cs:58`.
   That call site is where the stage was dropped. Fix 3.
3. **The plan is computed before consent and never revised.** All three suppression exits
   return the same action. A no-consent resident gets `start_cadence`; expected `no_op`. A
   no-consent record with a past move date throws instead of suppressing. Fix 3.
4. **The domain model was fitted to the sample** (EET, HB). Six fixed context fields. The
   hold-out carried eight more, all silently dropped. Two non-nullable dates defaulted to
   year 0001 and passed the planner's guard because both defaulted to the same value. Fix 2.
5. **The evaluator scores the agent against itself** (VF). No `send_at`, `subject`, `body`,
   `cta.options`, `cta.link`, or action payload. CTA type is compared to the constraint the
   template built it from. Personalization tokens are the two fields the template always
   inserts. Both checks cannot fail. Fix 1.
6. **Required states are literals** (VF, HR). `BrandStyleApplied: true` at line 83.
   `ConsentVerified: true` in the gate. Fair housing is "no keyword hit." Fix 7.
7. **Consent gate and channel selector compute the same thing twice**, and `.Value` at
   line 66 depends on a contract between them that the type system does not hold. Fix 3
   and D2, Sprint 4.
8. **The second safety validation at line 79 is provably redundant.** The validating
   composer already ran the same pure function on the same subject and body; line 77
   changes only `SendAt`. The branch at line 90 is reachable only by injecting a raw
   composer in a test. D7, Sprint 7.
9. **Three exits, three diagnostics shapes, no reason field.** No-consent, composition
   failure, and safety failure are indistinguishable in `diag.json`. Fix 7.
10. **Composition ignores what the oracle varies on** (LC). No options on SMS, no link on
    email, one subject for every email, English for the Spanish record. The OpenAI prompt
    never receives persona, stage, unit, dates, or language. Structured Outputs constrain
    the CTA to the record's own constraint. Fix 6.
11. **`send_at` is anchored on a field 10 records lack.** The oracle's dates cluster on
    2025-12-09, implying a batch reference time the agent is never given. Minute offsets
    are not derivable from any field. Fix 5.
12. **Logging records exceptions, not decisions** (HR). Every agent line carries
    `TaskId=x TaskId=x` because two scopes push the same key. The seven year-0001 plans
    produced no log line. The ingestion warning has no task id or line number. A legitimate
    `"none"` channel prints a 20-line stack trace. Fix 8 and Fix 4.
13. **The planner throws on ordinary data.** A past move date is an
    `ArgumentOutOfRangeException`, which fails the record with exit code 2. The repo's own
    rule is `Result` for expected failure. Fix 3.
14. **No step structure in the one file that must be narratable.** Six decisions in 40
    lines with nothing naming them. D7, Sprint 7.
15. **Coverage could not catch any of this.** Every decision-layer fixture is sample 1 with
    knobs; "resident" appears in tests only as a string. A branch on stage has no line, so it
    has no coverage figure. Fix 9.
16. **The rehearsal found it and it was parked.** The 11-record synthetic file surfaced
    year-0001 dates and `primary_cta` suppression on the same stages the hold-out used.
    TalkingPoints logs both as "deliberately not fixed."

## 3. Verdicts reached

- **Curveball or full miss: partial.** The dimension (persona, stage) was visible in both
  samples, in the design doc's own input table, in the `prospect_` prefixes, in the
  `reply_classification_f1_min` threshold, and in the synthetic rehearsal. The values
  (`reset_cadence`, `branch_on_intent`, `lease_end_date`, minute offsets, Spanish) were not
  knowable. The design noticed the dimension and left no slot for it. The process miss,
  parking the rehearsal finding, weighs more than the design miss.
- **Where it went wrong: ordering and the definition of "working," not the absence of
  rules.** Rules existed (TDD, coverage gate, SOLID and DRY, sprint cadence) and were
  followed. They governed shape. Nothing governed truth against data. The agent met the
  definition of working it was given.
- **What SOLID and DRY on everything cost: explainability, not correctness.** Collapsing
  every interface would still yield 2 of 12. Two failures, two fixes.
- **The blank at "how does it work" was a code-shape finding.** The system is one 40-line
  method. Nothing pointed at it.
- **Production-shaped parts are real:** secrets in user-secrets, per-record isolation,
  correlation id on log lines, bounded retry, safety gate on every exit. The decision layer
  is the scoped defect.
- **Interfaces belong on boundaries.** Playbook step 47 (the smallest seam per external or
  non-deterministic dependency, real plus offline implementation) is satisfied here only by
  the composer, the completion client, and the safety validator. The eight single-
  implementation interfaces are what step 47 excludes. The one non-deterministic dependency
  with no interface is the clock; D7 passes the reference time as a value instead.

## 4. Rules and advice adopted today

Project-agnostic versions live in the playbook and its appendices. Applied to this repo:

- Narrate one record through the code aloud before any PR merges. In AGENTS.md as a
  workflow step.
- A rehearsal-set failure blocks the sprint.
- The scorer is complete and proven able to fail before any agent change is measured.
- Optional in real data means nullable, and absence is logged per record.
- A planner with no rule for an input returns "no policy," never the nearest rule.
- One log line per record with decision inputs and defaulted fields. One scope owner.
- Every surprise becomes a fixture the same day.
- After fixing against the 12-record file, it is a training set. Keep a second synthetic set
  untouched for the honest number, and label which number is which.
- Do not rewrite to collapse interfaces up front; let the fixes delete them.
- One comment per step in the orchestrator naming the step, matching the DESIGN.md flowchart.
- Do not treat the interview blank as an architecture knowledge gap.

## 5. Definitions given today

Recorded once in the playbook's Appendix A: hold-out, synthetic hold-out, overfitting,
coverage, mutation testing, Goodhart's law, tautological test, fitted versus learned, CI,
CD, branch protection. The interviewer's word for coverage failing was most likely
"overfitting"; alternatives were mutation testing, Goodhart, and tautological tests.

## 6. The nine fix items

Listed here so this file is complete:

1. Complete the evaluator: `send_at` to the day and hour window, CTA type against
   `expected.cta.type`, presence of options or link when expected, action payload.
2. Nullable dates, observed optional fields on the context record, per-record report of
   absent and unrecognized fields; a reflection test that fails on any value-type record
   property that is neither `required` nor nullable.
3. Planner keyed on persona, lifecycle stage, and consent outcome; action payload shapes;
   `Result` instead of throw; plan after the consent check.
4. A nameable no-channel value so `"none"` round-trips.
5. Scheduler takes a run reference time; `last_interaction` is a floor; minute offsets
   recorded as under-determined.
6. Composer shaped by stage, channel, and language; options on SMS, link on email, subject
   per stage, Spanish variant; missing `primary_cta` means no fixed CTA; OpenAI prompt
   carries persona, stage, unit, dates, language.
7. Earn the three required states; diagnostics carry a suppression reason.
8. One scope owner; decision-input log line per record; line number on the ingestion
   warning; `"none"` channel is one Information line.
9. Fixtures shaped like the hold-out; a synthetic 12-record golden test through the
   evaluator; CI running `test.ps1` on every push.

## 7. Plan to completion

Two parts, named by the playbook phase they belong to so the numbering cannot drift.
Decisions (playbook Phase 0, steps 9 to 13, redone) make every choice that scopes the rest
and write it into DESIGN.md before any code changes. Sprints (playbook Phases 2 through 9)
implement decisions already made, one sprint per PR, in dependency order. Repo workflow applies throughout: strict TDD, one sprint at a
time, all work on `dev`, `.\test.ps1` green at the end of every cycle.

### Decisions (playbook Phase 0)

Eight decisions. Each states the question, the options, the recommendation, and what it
scopes. Your call on each; the recommendation is the default if you say nothing. All eight
land in DESIGN.md (sections 2, 3, 6, 9 rewritten) in Sprint 1, which closes Phase 0, and every rule in section 3
cites the input field it keys on.

**D1. Input contract.** What the reader accepts, what it reports, what it refuses.
- Required fields (record is an error row without them): `task_id`, `persona`,
  `lifecycle_stage`, `consent`, `channel_preferences`, `input.property_name`,
  `input.timezone`, `input.language`, `input.profile.first_name`.
- Optional and nullable, absence logged per record: `move_date_target`,
  `last_interaction`, `missed_tour_time`, `cancellation_reason`, `lease_end_date`,
  `move_in_date`, `unit`, `renewal_offer_id`, `constraints.primary_cta`,
  `constraints.respect_consent`, `constraints.locale_applied`.
- The rule behind the two lists, enforced for every field: a value-type property on an
  input record is either `required` (the C# modifier; System.Text.Json throws on a missing
  member, which becomes the error row) or `Nullable<T>`. A reflection test in the suite
  walks every record type under `Domain/` and fails on a value-type property that is
  neither, so the next optional field cannot be added non-nullable by omission. The eleven
  names above are today's instances; the test is what holds for the twelfth.
- Unknown members: log at Information per record, do not reject. Rejecting would drop
  records on the next unseen file; logging keeps them and makes the gap visible.
- Reference time: a `--now` CLI flag; default is the latest `last_interaction` in the
  batch, else UTC now. Logged once per run.
- Landed 2026-09-07 on this branch, PR #14 (PR #1 review fixes, Phase 0 gate
  overridden by Andrew): absence of any required member is a failure row via
  `RespectRequiredConstructorParameters` on `AgentJsonOptions.Default` rather than the
  `required` modifier, which positional records cannot carry; the reader returns one
  `Result` per line and `CliRunner` counts a failure row toward exit code 2. Deliberate
  deviations from the lists above: `move_date_target` and `last_interaction` are required
  for now, since what the planner and scheduler do without them is D2 and D4 and neither
  is built; the reflection test, the per-record log of an absent optional field,
  unknown-member logging, and `--now` remain Sprint 3.
- Scopes: D2, D4, D5, Fix 2.

**D2. Decision model.** How `next_action` is chosen.
- Order: consent first. Not contactable yields `no_op` with reason `no_contact_consent`
  and nothing else runs.
- A policy table keyed on `(persona, lifecycle_stage)`, one row per stage observed (nine
  today), each row carrying the action template and the stage's default CTA type. Table is
  data in one file, not branches spread across classes.
- Horizon (`move_date_target` minus reference time) is read only inside the prospect `new`
  and `open` rows to choose short versus long cadence and follow-up days.
- Absent `move_date_target` in a `new` or `open` row: the action type is unchanged, since
  the oracle keys type on stage alone (appendix table); the horizon-dependent parameter
  takes the row's default, recorded in the policy table and labeled under-determined; the
  record's log line names the defaulted field. Not an error row: the stage rule exists,
  only its parameter is missing an input.
- No row for the key: `Result` failure "no policy for persona/stage". The record becomes an
  error row and a log line. The planner never applies the nearest rule.
- `NextAction` becomes one record with `Type` and nullable `Name`, `Value`, `InDays`,
  `Mapping`, `Reason`, nulls omitted on the wire. No discriminated union; one record is
  enough for six shapes.
- Consent gate and channel selector merge into one `Select` returning an option; contactable
  is "has a value". Two classes were computing one question.
- Scopes: Fix 3, Fix 7's reason field, the orchestrator reorder.

**D3. Output contract.** What suppression looks like and what diagnostics carry.
- `CommunicationChannel.None`, serialized `"none"`. A suppressed record emits a
  `next_message` object with channel `none` and null fields, matching the oracle's shape,
  instead of a null object. `Body` becomes nullable.
- Diagnostics gain `suppression_reason`: `none`, `no_consent`, `composition_failed`,
  `safety_violation`, `no_policy`.
- Required states are earned: `consent_verified` is true only when the gate evaluated the
  record; `fair_housing_check_passed` stays the validator's verdict; `brand_style_applied`
  becomes a check that can fail (property name present, channel-correct opt-out phrasing,
  no invented pricing pattern), applied to template and model output alike.
- Landed 2026-09-07 (same branch as D1's note): `CommunicationChannel.None`, serialized
  `"none"` and first in the enum so the default value is the absence; `Body` nullable; the
  validator and evaluator treat a null body as empty text; the evaluator scores the agent's
  null message, the oracle's `none`, and a null channel as one channel, so the suppressed
  oracle rows are scoreable. Still open: the agent's own suppressed output is a null object,
  not the object with `none`; `suppression_reason` and the earned states are unchanged.
- Scopes: Fix 4, Fix 7, the scorer's handling of the opt-out record.

**D4. Scheduling.** What `send_at` is a function of.
- `send_at` is the channel's default slot (sms 09:00, email 10:00, voice 09:00 local) on the
  first day at or after `max(reference time, last_interaction)` in the prospect's timezone.
- Per-stage day offsets seen in the oracle (renewal follow-ups five days out, welcome the
  next day) and minute offsets (09:05, 09:15, 13:00) are recorded in the assumptions log as
  under-determined by the input. Not modeled.
- Scored to the day and to a one-hour window, never to the minute.
- Scopes: Fix 5, D6's send_at rule.

**D5. Composition.** What the message is a function of.
- Template set keyed on `(lifecycle_stage, channel, language)`. English for all nine stages;
  Spanish for the prospect stages. A stage with no template falls back to the persona
  default and logs it.
- Channel shape: SMS carries numbered reply options in the body and `cta.options`; email
  carries a link and `cta.link` and a subject per stage. Link values are under-determined
  by the input; emit a path built from the property slug and CTA, and score presence, not
  value.
- Missing `primary_cta` means the policy row's default CTA, never suppression.
- CTA type vocabulary: the mapping table grows from the twelve records (`reschedule_tour`
  to `reschedule`, `reply_intent` to `intent_capture`). Labeled as fitted to the twelve.
- The model path is unchanged in principle (prose only) and changed in inputs: the prompt
  carries persona, stage, unit, lease end date, move-in date, language, and the policy
  row's CTA; Structured Outputs constrain `cta_type` to that CTA.
- Body semantics are not scored. An LLM judge is out of scope for completion.
- Scopes: Fix 6.

**D6. Evaluation contract.** What is scored and against which data.
- Scored per record: channel; `send_at` to day and hour window; action type and payload
  where the oracle has one; CTA type against `expected.cta.type`; CTA payload presence
  (options non-empty on SMS, link non-null on email); opt-out phrase; safety count;
  personalization as fact coverage including unit and stage facts; latency.
- Scorer proof, run in the suite: the labeled answers as actuals score 100 percent; one
  corrupted field scores a failure.
- Two datasets, both reported, labels fixed: the twelve-record file is the training set
  from Sprint 4 on; a second synthetic set, written in Sprint 2 before any decision code
  changes and never edited after, is the honest number.
- Scopes: Fix 1, Fix 9, the definition of complete.

**D7. Structure.** What stays, what goes, what the orchestrator reads like.
- Interfaces kept: composer, completion client, safety validator (each has a real and a
  substitute implementation). Reference time is passed as a value, not an `IClock`; a value
  parameter is the smaller abstraction and tests pass a fixed one.
- Interfaces removed as each sprint touches them: consent gate, channel selector,
  scheduler, planner, agent, evaluator, record reader, record writer. Concrete classes.
- Orchestrator: six steps, one comment naming each, plan after consent, one validation, one
  exit builder that takes a reason. Output records (`AgentOutput`, `NextMessage`, `Cta`,
  `NextAction`) move into one file, since they are one concept: the output contract.
- Landed 2026-09-07 (same branch as D1's note): `IRecordReader` removed, `JsonlRecordReader`
  is concrete. The rest of the list is unchanged.
- Scopes: Sprint 7, the narration.

**D8. Gates.** What runs without a person remembering.
- `.github/workflows/test.yml` runs `dotnet build` and `.\test.ps1` on every push and PR;
  branch protection on `dev` requires it.
- The synthetic golden test is in the suite, so a rehearsal failure is a red PR.
- `test.ps1` exits with `dotnet test`'s exit code. Until 2026-09-06 its pipeline ended in
  `Tee-Object`, so `powershell -File .\test.ps1` returned 0 on a red suite and a CI step
  calling it would have passed; found by the PR #1 review, fixed the same day. The same
  fix stops Windows PowerShell 5.1 wrapping each stderr line in a `NativeCommandError`
  block inside `test-output.txt`.
- AGENTS.md gains the narration rehearsal rule as a workflow step.
- Scopes: Sprint 1, Fix 9.

### Sprints (playbook Phases 2 through 9)

Each sprint implements decisions already written. Each names the check that proves it.

| Sprint | Implements | Proof |
|---|---|---|
| 1 Decisions and gates | D1 to D8 written into DESIGN.md; AGENTS.md rules and doc map; CI file; branch protection; commit today's docs | DESIGN.md section 3 cites a field for every rule; the doc PR shows the `test` check green |
| 2 Harness | D6: scorer fields, scorer proof test, second synthetic set written and frozen, stage-shaped fixtures | scorer proven able to fail; baseline numbers for both sets recorded in section 1 of this file |
| 3 Contracts | D1, D3, logging (Fix 2, 4, 8): nullable fields, per-record gap log, `None`, suppression reason, one scope owner, decision-input log line | hold-out log shows defaulted fields on ten records, no duplicated task id; opt-out record is a scored row; the nullability test was seen red against one non-nullable field before it went green |
| 4 Decision model | D2 (Fix 3): policy table, merged gate and selector, orchestrator reorder, `Result` from the planner | Action column OK on all twelve; no-consent plans `no_op`; past move date is a row, not exit 2 |
| 5 Scheduling | D4 (Fix 5): reference time, floor, assumptions log | `send_at` OK to day and hour on the 2025-12-09 records; the rest listed with the missing field |
| 6 Composition and states | D5, D3 states (Fix 6, 7): template set, channel shape, Spanish, default CTA, prompt inputs, earned states | both suppressed emails compose; CTA payload checks pass; `diag.json` names every suppression reason |
| 7 Structure and narration | D7: interface removal, one output file, orchestrator step comments; `docs/NARRATION.md` filled and spoken; DESIGN.md final; both numbers final | CI green; narration delivered without notes; the appendix of this file carries before and after for both sets |

**Taken early, 2026-09-07.** The PR #1 review (ten findings, all posted on the PR) was
fixed on this branch, PR #14, before Phase 0 closed, on Andrew's explicit override of
the AGENTS.md gate: a deliberate PF violation, recorded here. It took from Sprint 3 the
required-member enforcement, the per-line `Result`, `None`, and the nullable body (D1, D3),
and from Sprint 7 the record reader interface (D7). It also fixed `test.ps1`'s exit code
(D8, on PR #14), the two dead `JsonPropertyName` attributes, the untested blank-line
counting, and the missing complexity line on `ReadAll`. The sprint rows above keep what
remains.

**Definition of complete.** The twelve-record file scores 12 of 12 on every field in D6,
labeled "trained on the twelve." The frozen synthetic set has its own reported number. CI
is green on `dev` and required to merge. DESIGN.md cites a field for every rule and lists
every under-determined item. `docs/NARRATION.md` is filled and has been spoken aloud.

## 8. Files written or changed today

Repo (branch `retrospective-2026-09-06`, PR #14 into `dev`, branched from
`agent-instruction-files`, PR #13): this file; `AGENTS.md` (Current phase, doc map,
narration rule) and its generated copy; `TalkingPoints.md` rewritten as one sentence per
point; `docs/BACKLOG.md` deleted; two untracked drafts, `REBUILD_PLAN.md` and
`docs/HOLDOUT_REVIEW.md`, discarded without ever being committed, the second merged into
the appendix below. No changes under `src/` or `tests/`.

`~/.agent-rules` (branch `ordered-work-and-playbook-audit`, committed): `CODE_PILLARS.md`
(Pillar 3, VF for inferred rules),
`PROJECT_PLAYBOOK.md` (27 steps rewritten, three appendices), `templates/AGENTS.md`
(Current phase, Next step), `README.md`.

Claude memory: pointers for the narration guide, the no-CI/CD background, decisions before
tasks, and pasted output as evidence.

## 9. Open decisions, yours

- The CI workflow file and the branch protection rule (D8, Sprint 1). Not added; the file
  content is in the playbook's Appendix A under CI.
- Whether the interview project restarts at Phase 0 step 1, in which case the AGENTS.md next
  step moves from 4 to 1.

## Appendix: hold-out evidence

From a fresh run on 2026-09-06 against the real 12-record hold-out (kept outside the repo):

```
dotnet run --project src/Agent.Cli -- --input real_holdout_12.jsonl --output out.json --diagnostics diag.json --eval-report eval.txt --log-file run.log
```

### Per record: what the evaluator said versus what came out

| Record | Evaluator said | What the output actually is |
|---|---|---|
| prospect_welcome_day0 | PASS | Correct channel, send_at, action, CTA type. Body has no options; expected has `["Thu","Fri"]`. |
| prospect_long_horizon_day3 | PASS | `send_at` is 2025-12-06, expected 2025-12-09. Wrong on one of the two training samples. Not scored. |
| prospect_no_show_reengage | FAIL (action) | `send_at` 0001-01-01. CTA `reschedule_tour` scored OK against expected `reschedule`. |
| prospect_cancellation_manager_cross_sell | FAIL (action) | `send_at` 0001-01-01. Subject "Tour Oak Ridge Apartments" for a cancellation cross-sell. |
| prospect_consent_block_sms_fallback_email | FAIL (channel) | Suppressed entirely because `primary_cta` is absent. Expected an email. |
| resident_renewal_90day_notice | FAIL (action) | `send_at` 0001-01-01. Body "Reply or click to review renewal". Subject "Tour Oak Ridge Apartments". |
| resident_renewal_undecided_followup | FAIL (action) | `send_at` 0001-01-01. CTA `reply_intent` scored OK against expected `intent_capture`. |
| resident_welcome_day0 | FAIL (action) | `send_at` 0001-01-01. Body "Reply or click to get started". |
| resident_loyalty_engage | FAIL (action) | `send_at` 0001-01-01. |
| resident_opt_out_respected | ERROR (unscoreable) | Message correctly suppressed. `next_action` is `start_cadence` for a resident with no consent; expected `no_op`. |
| prospect_spanish_locale | FAIL (action) | `send_at` 0001-01-01. English prose with Spanish amenity nouns pasted in. |
| resident_renewal_details_branch_email | FAIL (channel, action) | Suppressed entirely because `primary_cta` is absent. Expected an email. |

Honest count if `send_at` were scored to the day: 1 of 12. Personalization reads 1.00 on
every row because the tokens are first name and property name and the template always
inserts both.

### What the hold-out's expected actions key on

| lifecycle_stage | expected next_action.type |
|---|---|
| new | start_cadence |
| open | follow_up_in_days |
| no_show | reset_cadence |
| cancelled_manager | follow_up_in_days |
| renewal_window | schedule_sms_reminder |
| renewal_undecided | branch_on_intent |
| renewal_details_requested | start_esign_flow |
| welcome, loyalty_engage | follow_up_in_days |
| any stage, no consent | no_op |

### Logging, from the same 53 log lines

Confirmed working: `--log-file` writes the same lines as the console sink; every per-record
line carries `TaskId=`; suppression reasons and compose failures are logged at Warning; the
unparsable `expected` logs at Warning with the full exception.

Defects visible in the same run:

1. Every line from `LeasingMessageAgent` carries `TaskId=x TaskId=x`: `CliRunner` and the
   agent both open a scope with the same key. The one test that checks rendering asserts
   `Contains("TaskId=t1")`, which a duplicated pair satisfies.
2. The seven year-0001 plans and the two suppressions for a missing optional constraint
   produced no log line about the missing fields. The log is complete for exceptions and
   empty for decisions.
3. The `expected` parse warning has no task id and no line number; it fires during ingestion
   before any scope is open.
4. A legitimate `"none"` channel prints a 20-line stack trace for an expected absence.

### Why coverage could not catch any of it

`SampleProspectCases.Minimal` is sample record 1 with knobs. Every decision-layer test is a
prospect, stage new, with a move date and a last interaction. The word "resident" appears in
tests only as a string in evaluator, formatter, reader, and CLI tests. 176 tests cover 100
percent of the lines that exist. A branch on `lifecycle_stage` has no line, so it has no
coverage figure, so the gate is silent.
