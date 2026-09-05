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

CLI flags: `--input <jsonl>` `--output <json>` `[--composer template|openai]`
`[--diagnostics <json>] [--eval-report <txt>] [--log-file <log>]`. Exit codes: 0 success,
1 usage error, 2 partial failure. Full reference: `docs/OPERATIONS.md`.

The OpenAI key is `OpenAI:ApiKey` in `dotnet user-secrets` for `src/Agent.Cli`. The user
sets it. Never read, print, or write the value.

## Layout

- `src/Agent` library: `Domain/` records, `Decisions/` consent, channel, scheduler,
  planner; `Composition/` template and OpenAI composers; `Safety/` validator and
  compose-validate loop; `Orchestration/` `LeasingMessageAgent`; `Evaluation/` scorer;
  `Ingest/` reader and writer; `Common/` `Option`, `Result`, logging helpers.
- `src/Agent.Cli`: thin shell, `CliRunner` is the composition root.
- `tests/Agent.Tests`, `tests/Agent.Cli.Tests`: xUnit, fakes under `TestSupport/`.
- `docs/DESIGN.md` architecture and assumptions log. `docs/BACKLOG.md` sprint plan.
  `docs/CODE_REVIEW.md` review angles and deliberate scope decisions. `TalkingPoints.md`
  per-sprint decision log.

## Workflow

- Strict TDD: failing test first, confirm the failure, then implement. No implementation
  without a failing test behind it.
- Every cycle ends with `.\test.ps1` and reading `test-output.txt`. The gate fails the
  build under 100 percent; do not lower the threshold, exclude files, or add tests that
  exist only to hit a line.
- One sprint at a time. Do not start the next until the current one is green.
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
- Domain types never reuse a BCL name. Rename at the source, never alias.
- `ILogger<T>` is optional on every constructor and defaults to `NullLogger`. `TaskId` is
  a log scope value, never repeated in message text.
- `AgentLog` (static `AsyncLocal` factory accessor) is the one exception to constructor
  injection, and exists only for `LenientExpectedOutcomeConverter`. Do not add a second.
- No em dashes in code, comments, or docs.

## Gotchas

- Missing JSON properties on non-nullable value types (`DateOnly`, `DateTimeOffset`) do
  not throw. They default silently. This caused the real hold-out failures. Declare
  anything optional in real data as nullable.
- `expected.next_message.channel` can be `"none"` in real data. `CommunicationChannel`
  has no such member, so `LenientExpectedOutcomeConverter` sets `Expected` to null and
  the record scores as unscoreable.
- The safety validator's whole-word check calls static `Regex.IsMatch` per term (about
  25 terms) against a 15-entry cache, so patterns recompile on every message. Known,
  unfixed.
- Quiet hours and semantic fair-housing checks are deliberate scope-outs. See
  `docs/CODE_REVIEW.md` before flagging either.

## Review criteria

Reviews check correctness first, then the pillars in `~/.claude/CLAUDE.md` by acronym
(VF, LC, EA, SD, HR, SCU, EET, HSC, SCS, BC, HB). Report a finding only when it affects
correctness, a stated requirement, or a named pillar, and name which. Do not report
style preferences, hypothetical future needs, or requests for more abstraction, defensive
code, or tests for cases that cannot occur. A reviewer asked to find gaps will report
some in sound work; a finding without a named rule behind it is optional and should say
so. If no finding meets the bar, the entire review output is: Nothing to report.
