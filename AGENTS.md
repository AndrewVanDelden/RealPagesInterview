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

Phase 4 of `~/.agent-rules/PROJECT_PLAYBOOK.md`: Fuzzy and external components.
Phase 0 was restarted at step 1 on 2026-09-07 (D12) and passed the same day; Phase 1 passed on
2026-09-07 (CI green on PR #16, `dev` requires the `test` check). Phase 2 passed on 2026-09-08
in Sprint 3: the labels of `sample.jsonl`, `holdout_12.jsonl`, and `synthetic_12.jsonl` passed
as actuals score 100 percent and one corrupted field per check scores a failure on that check;
both proofs run in the suite (`ScorerProofTests`). Steps 28 to 36 landed in Sprint 3; step 31,
the model judge, is deferred to Sprint 6 (D15); step 37, a sidecar, is not needed. Steps 38 to
43 landed in Sprint 4: the catalog keyed on persona and lifecycle stage with its generic row,
the whole action vocabulary in `ActionTypes`, the `Result` on `ActionCatalog.Create` rather
than on `Plan` (D18), and `diagnostics.action_plan`.
Phase 3 passed on 2026-09-08 in Sprint 5, steps 44 to 46. Every check the deterministic core
owns is perfect on the synthetic set run with the template composer, which is the fuzzy part
stubbed: channel 12 of 12, day 10 of 10, hour 10 of 10, action 12 of 12, opt-out 10 of 10,
call-to-action type 10 of 10, safety 12 of 12, personalization 9 of 9, and exit code 2 for the
malformed line by design. The two checks that fail, payload 0 of 10 and language 9 of 10, are
composition, which Sprint 6 owns and D5 scopes; no decision-core output is unexpected. On the
diagnostics half of the check, every decision whose working is not readable from the input and
the output has an account: consent (`consent_verified`, `suppression_reason`), the action
(`action_plan`, D18), and the send (`schedule`, D22). The channel has none, and D23 is why:
its working is the record's own ordered `channel_preferences` intersected with its `consent`,
which the input states and the output's channel answers, so it is not internal to a component
the way the other three are. D23 is closed, confirmed by the requester; do not re-open it by
adding a channel notes object.
Steps 44 to 46 landed in Sprint 5: the send slot resolved against the zone's rules rather than
stamped with an offset the zone never had (D21, A20), `diagnostics.schedule` naming the floor,
the zone and the slot (D22), and the property tests of step 45, which sweep every system zone
at both send hours across every 2026 transition and assert that the offset stamped is the one
the zone was on and that the send never lands before the slot it was asked for. Both transition
branches are unreachable from the current zone database at 09:00 and 10:00, so they are proved
against custom zones built in `tests/Agent.Tests/TestSupport/SlotResolutionTestZones.cs`. Every
tally on all three sets is unchanged, which is what a rule no record reaches should do.
Check for Phase 4: with the network disabled, the product completes the full example set using
the offline path and the diagnostics say so on every record. Status: not passed; nothing in the
diagnostics yet records which composer produced a message (playbook step 57).
Next step: 47 to 60, Sprint 6, Composition (the Sprint 6 row of `docs/DESIGN.md` section 9, D5,
D19): facts, language handling, the catalog's default call-to-action column, the model prompt
inputs and its untrusted-data boundary, and the body judge deferred from Sprint 3 (D15).
Open decisions: none; S1 to S4 and D1 to D23 are in `docs/DECISION_LOG.md`.
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
