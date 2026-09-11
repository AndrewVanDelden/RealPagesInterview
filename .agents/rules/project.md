---
trigger: always_on
glob:
description: Project instructions for this repository. GENERATED COPY of AGENTS.md; edit AGENTS.md and run .\sync-agent-rules.ps1
---
# Next-Best-Message Agent

Context-aware message-sending agent for a leasing take-home. Reads JSONL prospect records,
decides whether, how, when, and what to communicate, and emits `next_message` plus
`next_action`. Deterministic pipeline in code; the LLM writes prose only, behind an
interface. The interview is over; work here is portfolio quality, not graded.

Universal code rules live at user scope (`~/.claude/CLAUDE.md`). This file holds only
what is specific to this repo.

This file is rules, not state: it reads the same every session and changes only by a deliberate edit
reviewed like code. Where the project has got to is the top of `docs/DECISION_LOG.md`, replaced every sprint.

## Build, test, run

```
dotnet build                      # whole solution (.slnx)
.\test.ps1                        # dotnet test with the 100% line/branch/method coverage gate; tees to test-output.txt
dotnet run --project src/Agent.Cli -- --input sample.jsonl --output out.json
```

Every flag and both output files: `docs/OPERATIONS.md`. Exit codes are 0 success, 1 usage
error, 2 partial failure. `--now` is the run's reference time (D10), defaulting to now; the
documented runs pass `2025-12-09T00:00:00-06:00` for `holdout_12.jsonl` and
`2026-03-07T12:00:00Z` for `synthetic_12.jsonl`.

The OpenAI key is `OpenAI:ApiKey` in `dotnet user-secrets` for `src/Agent.Cli`. The user
sets it. Never read, print, or write the value.

## Layout

- `src/Agent` library: `Domain/` records; `Decisions/` channel, scheduler, planner
  and the action catalog (D17); `Composition/` both composers, the call-to-action catalog and
  the per-language sets; `Safety/` validator and compose-validate loop; `Orchestration/` the
  agent; `Evaluation/` scorer and judge; `Ingest/` reader, writer; `Common/` shared types.
- `src/Agent.Cli`: thin shell; `CliRunner` is the composition root.
- `tests/Agent.Tests`, `tests/Agent.Cli.Tests`: xUnit, fakes under `TestSupport/`.
- `docs/`: `DECISION_LOG.md` the current phase and one paragraph per sprint, `DECISIONS_ARCHIVE.md`
  every decision in full, `DESIGN.md` the rules, their evidence and the numbers, `CODE_REVIEW.md` the scope-outs.
- `sample.jsonl` is the only evidence any rule is fitted to; `holdout_12.jsonl` and
  `synthetic_12.jsonl` are evaluation sets, run and reported, never fitted to (D9, D6).

## Workflow

- Strict TDD: failing test first, confirm the failure, then implement.
- Every cycle ends with `.\test.ps1` and reading `test-output.txt`. Never lower the 100
  percent threshold, exclude a file, or add a test that exists only to hit a line.
- One sprint at a time. Do not start the next until the current one is green.
- Before a PR merges, narrate one record through the code aloud, file by file, without notes
  (playbook Appendix B). A hop through an interface to its only implementation is a finding.
- All work on `dev`, never `main`. One PR per sprint, `gh pr create` against `dev`.
- No edits under `src/` or `tests/` while the phase is 0 or 1.
- Every substantive decision, bug, or run/debug fact lands in `docs/DECISIONS_ARCHIVE.md` before
  the turn ends, named in the log's sprint paragraph, never in this file. Assume the chat can be cleared at any time.
- After any edit to this file, run `.\check-instruction-files.ps1`, which CI also runs (four rules: rules only,
  150 lines, the phase state in the log, the log's word cap, every citation resolving), then `.\sync-agent-rules.ps1`,
  which regenerates `.agents/rules/project.md` for Antigravity. Never edit the generated copy.

## Conventions that differ from defaults

- JSON is snake_case via `AgentJsonOptions.Default` (`JsonNamingPolicy.SnakeCaseLower`).
  Add `[JsonPropertyName]` only where the wire name is not the snake_case of the property.
- Output file is a single indented JSON array, not JSONL. Input stays JSONL.
- `[GeneratedRegex]` for every regex. No `new Regex(...)`.
- `Option<T>` and `Result<T>` from `Agent.Common` for expected absence and expected
  failure. Exceptions are for bugs.
- `JsonlRecordReader.ReadAll` returns one `Result<ProspectCase>` per non-blank line; a bad
  line is a failure row naming its line number and counts toward exit code 2, never an abort.
- Domain types never reuse a BCL name. Rename at the source, never alias.
- `ILogger<T>` is optional on every constructor and defaults to `NullLogger`. `TaskId` is
  a log scope value, never repeated in message text.
- `AgentLog` (static `AsyncLocal` factory accessor) is the one exception to constructor
  injection, and exists only for `LenientExpectedOutcomeConverter`. Do not add a second.

## Gotchas

- Only `task_id`, `consent` and `channel_preferences` are required (D1); every other member
  is nullable with a `= null` default, and a decision needing one applies the assumption that
  names the default (DESIGN.md section 7). Undeclared members at any depth are kept and listed
  by path. A non-nullable value-type member on an input record defaults silently: refuse it.
- `CommunicationChannel.None` is first so the default is absence, and `Unknown`, any channel
  name the program does not know, is never opted in. A suppressed record emits `next_message`
  with channel `none` and null members; the evaluator scores that, a null message and a null
  channel alike. An `expected` that cannot parse leaves the record unscoreable.
- The reference time is a value: `--now` on the CLI, a parameter on the agent, the planner and
  the scheduler. Nothing in `src/Agent` reads a clock.
- The evaluator scores against the label, never the product's own tables (D13 d). Checks are
  `Passed`, `Failed` or `NotMeasured`, and not measured never counts as a pass.
  `BaselineNumbersTests` pins every tally: a rise is recorded, a drop fails the build.
  `OptOutInstructions` is the one opt-out definition for both the validator and the scorer.
- The output file carries no task id, so `--replay` pairs rows with parsed records by position
  and refuses a count mismatch with exit code 1 (D14).
- Only `CliRunner` opens the `TaskId` log scope (D16). Do not add one in the library.
- Each safety check answers for itself (D38) and all four are hard gates (D39). D40 says which
  a record may switch off; the exempt lists are spans, never terms (D41, D45).
- Every action type the program can emit is a constant on `ActionTypes`, and
  `ActionCatalog.Create` refuses a row whose type is not in `ActionTypes.All`, so a new action
  type is added there first (D17). `Default` goes through `Create` like every other catalog.
- `generic_row_no_branch` (a row matched but states no action for that branch) and
  `generic_row_no_match` (no row for that persona and stage) are different facts, not errors.
- The planner does not return a `Result`; `ActionCatalog.Create` does (D18). Adding a
  per-record failure to `Plan` re-opens a closed decision.
- `SlotResolution.ShiftedPastGap` and `EarlierOfTwo` cannot fire on the current zone database
  (A5), and are proved against the custom zones in `SlotResolutionTestZones.cs` plus a sweep of
  every system zone. Neither dead code nor a gamed gate (D21, A20).
- A golden test normalizes line endings on both sides. A raw string literal carries whatever
  endings git checked the file out with, so a golden compared raw passes on one checkout and
  fails on another.
- `Attempts` is stamped by the compose-validate loop, never at a composer (D24 addendum), and
  `CompositionNotes` is `(Composer, Attempts, LocaleApplied)` and nothing else. The two spend
  counts ride `ComposeOutcome` to the wire on `AgentDiagnostics`, so a null `composition` does
  not take them with it (D66); the loop sums them and `CliRunner` owns the batch total.
- Every deliberate scope-out is in `docs/CODE_REVIEW.md`. Read it before flagging one.

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
