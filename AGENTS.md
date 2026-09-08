# Next-Best-Message Agent

Context-aware message-sending agent for a leasing take-home. Reads JSONL prospect records,
decides whether, how, when, and what to communicate, and emits `next_message` plus
`next_action`. Deterministic pipeline in code; the LLM writes prose only, behind an
interface. The interview is over; work here is portfolio quality, not graded.

Universal code rules live at user scope (`~/.claude/CLAUDE.md`). This file holds only
what is specific to this repo.

## Build, test, run

```
dotnet build                      # whole solution (.slnx)
.\test.ps1                        # dotnet test with the 100% line/branch/method coverage gate; tees to test-output.txt
dotnet run --project src/Agent.Cli -- --input sample.jsonl --output out.json
```

CLI flags: `--input <jsonl>` and one of `--output <json>` or `--replay <json>`;
`[--now <ISO-8601>]` `[--composer template|openai]` `[--diagnostics <json>]`
`[--eval-report <txt>]` `[--log-file <log>]`. Exit codes: 0 success, 1 usage error, 2 partial
failure. `--now` is the run's reference time (D10), default the current UTC time; the run
against `holdout_12.jsonl` passes `2025-12-09T00:00:00-06:00`, against `synthetic_12.jsonl`
`2026-03-07T12:00:00Z`. `--replay` re-scores an existing output file without running the
agent (D14). Full reference: `docs/OPERATIONS.md`.

The OpenAI key is `OpenAI:ApiKey` in `dotnet user-secrets` for `src/Agent.Cli`. The user
sets it. Never read, print, or write the value.

## Layout

- `src/Agent` library: `Domain/` records, `Decisions/` consent, channel, scheduler,
  planner, and the action catalog (`ActionTypes`, `ActionCatalog`, D17); `Composition/` template and OpenAI composers; `Safety/` validator and
  compose-validate loop; `Orchestration/` `LeasingMessageAgent`; `Evaluation/` scorer;
  `Ingest/` reader and writer; `Common/` `Option`, `Result`, logging helpers.
- `src/Agent.Cli`: thin shell, `CliRunner` is the composition root.
- `tests/Agent.Tests`, `tests/Agent.Cli.Tests`: xUnit, fakes under `TestSupport/`.
- `docs/DESIGN.md` problem, inputs, rules with evidence, architecture, evaluation contract,
  assumptions log, sprint plan. `docs/DECISION_LOG.md` every decision in the recording form
  (S1 to S4, D1 to D12). `docs/CODE_REVIEW.md` review angles and deliberate scope decisions.
  `TalkingPoints.md` one sentence per decision, the walkthrough.
  `docs/RETROSPECTIVE_2026-09-06.md` why the hold-out scored 2 of 12; its plan is superseded.
- `sample.jsonl` the two given records, the only evidence rules are fitted to.
  `holdout_12.jsonl` the twelve-record evaluation set: run and reported, never fitted to (D9).
  `synthetic_12.jsonl` twelve records plus one malformed line, one per item of DESIGN.md
  section 4, labeled from the assumptions log, frozen since 2026-09-08 (D6).

## Current phase

Phase 5 of `~/.agent-rules/PROJECT_PLAYBOOK.md`: Safety, security, and compliance, with one
step of Phase 4 still open and blocked on the requester (step 60, named below).
Phase 0 was restarted at step 1 on 2026-09-07 (D12) and passed the same day; Phase 1 passed on
2026-09-07 (CI green on PR #16, `dev` requires the `test` check). Phase 2 passed on 2026-09-08
in Sprint 3: the labels of all three sets passed as actuals score 100 percent and one corrupted
field per check scores a failure on that check, both proofs in the suite (`ScorerProofTests`).
Phase 3 passed on 2026-09-08 in Sprint 5: every check the deterministic core owns is perfect on
the synthetic set with the composer stubbed, and every decision whose working is not readable
from the input and the output has a diagnostics object (D18, D22, D23).
Phase 4 passed on 2026-09-08 in Sprint 6, steps 47 to 60. Its check was run, not argued: all
three sets complete with outbound HTTPS blocked at the process level, exit 0, 0 and 2 (the
synthetic malformed line, by design), and `diagnostics.composition` names `template` as the
composer on every record that has a message. The three records reading null are the ones the
consent gate suppressed, which have no message and so no composer, the same rule
`action_plan` and `schedule` follow.
Steps 47 to 60 landed in Sprint 6: the composer's own working in the diagnostics (D24), the
call-to-action catalog and the link built from the property slug (D25, A21), language sets with
no allowlist anywhere (D26), the real client on the official OpenAI package pinned at 2.13.0
with one retry, a per-attempt timeout that divides the batch's strictest stated budget, and a
counted retry (D27,
D28), the model prompt's full field list and its untrusted-data boundary pinned by golden tests
(D29), and the reference-based judge behind `--judge` (D30, closing D15). Numbers moved on both
evaluation sets, and only from rules earned on the fitting evidence and the synthetic set:
payload 0 to 11 of 11 and language 10 to 11 of 11 on the hold-out, payload 0 to 10 of 10 and
language 9 to 10 of 10 on the synthetic set, and records passing every check 1 to 4 of 12 and
2 to 12 of 12. Every tally is in `docs/DESIGN.md` section 9 and pinned in `BaselineNumbersTests`.
One step of Phase 4 is open and needs the requester: step 60, the live model run against the
examples with both scores recorded side by side. It spends the OpenAI key, so it is not run
without being asked. Two facts to carry into it: one call and its retry are bounded by the
strictest stated `p95_latency_ms`, 2000 ms on these sets, so a slower call times out and the
fallback shows up as `composer: template` in the diagnostics (D28, and its addendum for what
that bound does and does not cover); and the same run under `--judge` is the first
measurement the body has ever had, since the personalization proxy reads 1.00 on every template
message by construction.
Check for Phase 5: every validator has a passing test, a failing test, and a false-positive
test. Status: not passed; the safety validator has passing and failing tests and no
false-positive tests, the required states of D3 are still claimed rather than earned, and
`brand_style_applied` is still hardcoded true (playbook step 66).
Next step: 61 to 71, Sprint 7, Safety and states (the Sprint 7 row of `docs/DESIGN.md` section 9,
D3): one validator per stated constraint with its own result, the hard-gate or soft-flag decision
written down per validator, an allow-list for legitimate uses a keyword proxy would catch, the
earned states in the diagnostics, and a review-queue record on a final validation failure.
Open decisions: none; S1 to S4 and D1 to D30 are in `docs/DECISION_LOG.md`.
No edits under `src/` or `tests/` while the phase is 0 or 1. Exception on record: the PR #1
review fixes (on PR #14's branch, 2026-09-07) edited both on an explicit user
override, a deliberate PF violation; what landed and what stayed deferred is under D1, D3, and
D7 in the retrospective.
Update this section at the end of every sprint. It is the first thing an agent reads.

## Workflow

- Strict TDD: failing test first, confirm the failure, then implement. No implementation
  without a failing test behind it.
- Every cycle ends with `.\test.ps1` and reading `test-output.txt`. The gate fails the
  build under 100 percent; do not lower the threshold, exclude files, or add tests that
  exist only to hit a line.
- One sprint at a time. Do not start the next until the current one is green.
- Before a PR merges: narrate one record through the code aloud, file by file, without
  notes (playbook Appendix B). A hop "through an interface to its only
  implementation" is a finding.
- All work on `dev`. Never commit to `main`. One PR per sprint, `gh pr create` against
  `dev`.
- Every substantive decision, bug, or run/debug fact lands in a repo doc before the turn
  ends. Assume the chat can be cleared at any time.
- After any edit to this file, run `.\sync-agent-rules.ps1`. It regenerates
  `.agents/rules/project.md`, the copy Antigravity injects (it needs `trigger: always_on`
  frontmatter, which this file cannot carry). Never edit the generated copy.

## Conventions that differ from defaults

- JSON is snake_case via `AgentJsonOptions.Default` (`JsonNamingPolicy.SnakeCaseLower`).
  Add `[JsonPropertyName]` only where the wire name is not the snake_case of the property.
- Output file is a single indented JSON array, not JSONL. Input stays JSONL.
- `[GeneratedRegex]` for every regex. No `new Regex(...)`.
- `Option<T>` and `Result<T>` from `Agent.Common` for expected absence and expected
  failure. Exceptions are for bugs.
- `JsonlRecordReader.ReadAll` returns one `Result<ProspectCase>` per non-blank line. A bad
  line is a failure row naming its line number; the file is never aborted, and `CliRunner`
  counts the row toward exit code 2.
- Domain types never reuse a BCL name. Rename at the source, never alias.
- `ILogger<T>` is optional on every constructor and defaults to `NullLogger`. `TaskId` is
  a log scope value, never repeated in message text.
- `AgentLog` (static `AsyncLocal` factory accessor) is the one exception to constructor
  injection, and exists only for `LenientExpectedOutcomeConverter`. Do not add a second.
- No em dashes in code, comments, or docs.

## Gotchas

- Only `task_id`, `consent`, and `channel_preferences` are required (D1); `ProspectCase`
  has a `[JsonConstructor]` listing exactly those three and `AgentJsonOptions.Default` sets
  `RespectRequiredConstructorParameters`, so a line missing one is a failure row naming it.
  Every other member is nullable with a `= null` default; a decision that needs one applies
  the assumption that names the default (DESIGN.md section 7) and `IngestNotes.Describe`
  lists the field per record. Undeclared members at any depth land in `UnknownMembers`
  (`[JsonExtensionData]`) and are listed by path. Silent year-0001 dates were the real
  hold-out failure; a non-nullable value-type member on an input record is the bug to
  refuse in review.
- `CommunicationChannel` has `None` (first, so the default is the absence) and `Unknown`
  (any name the file uses that the program does not know; never opted in). A suppressed
  record emits a `next_message` object with channel `none` and null members, the oracle's
  own spelling; the evaluator scores that object, a null message, and a null channel as
  one value. `expected` that cannot parse at all still makes
  `LenientExpectedOutcomeConverter` set `Expected` to null and the record is unscoreable.
- The reference time is a value: `--now` on the CLI, a parameter on the agent, the planner,
  and the scheduler. Nothing in `src/Agent` reads a clock.
- The evaluator scores against the label, never against the product's own tables: the
  call-to-action type is the label's `cta.type` (D13 d). Every check is `Passed`, `Failed`, or
  `NotMeasured`; not measured never counts as a pass, and the baseline tallies in
  `BaselineNumbersTests` are measurements that a rise updates and a drop fails.
  `OptOutInstructions` is the one opt-out definition for the validator and the scorer.
- The output file carries no task id, so `--replay` pairs rows with parsed records by position
  and refuses a count mismatch with exit code 1 (D14).
- Only `CliRunner` opens the `TaskId` log scope (D16). Do not add one in the library.
- The safety validator's whole-word check calls static `Regex.IsMatch` per term (about
  25 terms) against a 15-entry cache, so patterns recompile on every message. Known,
  unfixed.
- The action catalog is a compiled-in table, not a data file (D17). Every action type the
  program can emit is a constant on `ActionTypes`, and `ActionCatalog.Create` refuses a row
  whose type is not in `ActionTypes.All`, so a new action type is added there first.
  `ActionCatalog.Default` goes through `Create` like every other catalog.
- `generic_row_no_branch` and `generic_row_no_match` are different facts and the diagnostics
  keep them apart: the first is a row that matched but states no action for that horizon
  branch, because no sample showed one (prospect/new has no long branch, prospect/open no
  short one); the second is no row for that persona and stage at all. Neither is an error.
- The planner does not return a `Result`; `ActionCatalog.Create` does (D18). Adding a
  per-record failure to `Plan` re-opens a decision that was closed because the generic row
  makes every record classifiable.
- `SlotResolution.ShiftedPastGap` and `EarlierOfTwo` never fire on real data: no zone in the
  current database transitions across 09:00 or 10:00, the only two send hours (A5), so every
  record on every set reads `exact`. They are not dead code and the coverage gate is not being
  gamed: `TimeZones.ResolveSlot` is proved against the custom zones in
  `tests/Agent.Tests/TestSupport/SlotResolutionTestZones.cs`, whose transitions do cover the
  slot, plus a sweep of every system zone across every 2026 transition (D21, A20).
- `Scorecard` computes its per-check tallies and its p95 once, in field initializers, so a
  `with` copy that replaces `RecordScores` carries the old numbers into a report whose rows say
  otherwise. Build a new `Scorecard`; `SemanticJudge.JudgeAsync` does, and a test pins the
  tally after judging. A PR review caught this one, not the suite.
- Quiet hours and semantic fair-housing checks are deliberate scope-outs. See
  `docs/CODE_REVIEW.md` before flagging either.

## Review criteria

A review starts from the assumption that the diff has a regression, a gap, or a wasted
cost, and reaches "Nothing to report" only after an active search fails to find one. It is
not a check that the code matches its own description or that the author's own tests pass:
the author wrote the code, the tests, and the description, so confirming those against each
other only reproduces the author's blind spots. Coverage proves every line ran, not that
its arithmetic, its deleted behavior, or its edge cases are correct. A line that raises a
question is traced to an answer or checked with a scratch test, never waved through as
"probably intended" or "that's asking for defensive code."

Run these passes over the diff, not one narrative read:

- Contract and removed behavior: for every line the diff deletes or replaces, name what it
  guaranteed, and find where the new code re-establishes that guarantee. If you can't, that
  is a finding.
- State and arithmetic tracing: follow every counter, accumulator, and branch condition
  through at least one concrete scenario with real values. Code that reads correctly and
  code that computes correctly are different claims.
- Boundary and hostile inputs: name what happens on an invalid combination of inputs, an
  empty collection, a null on a path that looks unreachable, or calls made out of order.
- Access costs: check whether a property, getter, or formatter hides an allocation, a
  repeated scan, a re-sort, or I/O behind what reads like a cheap read.

Then check the pillars in `~/.claude/CLAUDE.md` by acronym (VF, LC, EA, SD, HR, SCU, EET,
HSC, SCS, BC, HB, PF, DBT; the key and the evidence for each are in
`~/.agent-rules/CODE_PILLARS.md`). Report a finding only when it affects correctness, a
stated requirement, or a named pillar, and name which. Do not report style preferences,
hypothetical future needs, or requests for more abstraction, defensive code, or tests for
cases that cannot occur. A reviewer asked to find gaps will report some in sound work; a
finding without a named rule behind it is optional and should say so. "Nothing to report"
is earned by running every pass above and finding nothing, never the default outcome of a
read that happened to feel clean.
