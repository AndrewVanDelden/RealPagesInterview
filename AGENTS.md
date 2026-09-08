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

CLI flags: `--input <jsonl>` `--output <json>` `[--now <ISO-8601>]` `[--composer template|openai]`
`[--diagnostics <json>] [--eval-report <txt>] [--log-file <log>]`. Exit codes: 0 success,
1 usage error, 2 partial failure. `--now` is the run's reference time (D10), default the
current UTC time; the run against `holdout_12.jsonl` passes `2025-12-09T00:00:00-06:00`.
Full reference: `docs/OPERATIONS.md`.

The OpenAI key is `OpenAI:ApiKey` in `dotnet user-secrets` for `src/Agent.Cli`. The user
sets it. Never read, print, or write the value.

## Layout

- `src/Agent` library: `Domain/` records, `Decisions/` consent, channel, scheduler,
  planner; `Composition/` template and OpenAI composers; `Safety/` validator and
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

## Current phase

Phase 2 of `~/.agent-rules/PROJECT_PLAYBOOK.md`: Scaffold and verification harness first.
Phase 0 was restarted at step 1 on 2026-09-07 (D12) and passed the same day; Phase 1 passed on
2026-09-07 (CI green on PR #16, `dev` requires the `test` check). Steps 25 to 27 landed in
Sprint 2 on 2026-09-08 (every member optional except three, per-record reader, output shape).
Check: the evaluator scores the golden expected outputs at 100 percent, and a deliberately
wrong output at less. Status: not passed.
Next step: 28, build the evaluator before the product (Sprint 3, the harness row of
`docs/DESIGN.md` section 9).
Open decisions: none; S1 to S4 and D1 to D12 are in `docs/DECISION_LOG.md`.
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
- The safety validator's whole-word check calls static `Regex.IsMatch` per term (about
  25 terms) against a 15-entry cache, so patterns recompile on every message. Known,
  unfixed.
- Quiet hours and semantic fair-housing checks are deliberate scope-outs. See
  `docs/CODE_REVIEW.md` before flagging either.

## Review criteria

Reviews check correctness first, then the pillars in `~/.claude/CLAUDE.md` by acronym
(VF, LC, EA, SD, HR, SCU, EET, HSC, SCS, BC, HB, PF, DBT; the key and the evidence for each are in
`~/.agent-rules/CODE_PILLARS.md`). Report a finding only when it affects
correctness, a stated requirement, or a named pillar, and name which. Do not report
style preferences, hypothetical future needs, or requests for more abstraction, defensive
code, or tests for cases that cannot occur. A reviewer asked to find gaps will report
some in sound work; a finding without a named rule behind it is optional and should say
so. If no finding meets the bar, the entire review output is: Nothing to report.
