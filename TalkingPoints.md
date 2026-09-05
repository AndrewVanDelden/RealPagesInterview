# Talking Points: Next-Best-Message Agent

Running log for the interview walk-through. One entry per sprint (see
[docs/BACKLOG.md](docs/BACKLOG.md) for the epic and sprint definitions).

- **Before:** what we are about to do and the decisions made going in, written
  before implementation starts.
- **After:** what we actually did and any decisions made or revised along the
  way, written once the sprint is confirmed green.

---

## Kickoff decisions (pre-Sprint 0)

- **Stack:** C# / .NET 10, newest native language features preferred over
  legacy patterns or external libraries (Pillar 2).
- **Terminology:** the whole backlog document is one Epic; each of its
  sections (formerly "Epic N") is a Sprint.
- **OpenAI:** key to be configured via `dotnet user-secrets` before Sprint 3.
  Model name defaults to `gpt-4o-mini`, held as a single config value so it is
  a one-line swap, not a redesign, if a newer model is available on the
  account when the key is set up. This default is a fact only about what
  existed as of prior knowledge, not a claim about what OpenAI currently
  offers.
- **Time budget:** Sprint 5 (working end-to-end CLI) is the hard floor for
  today's review. Sprint 6 (eval harness) and Sprint 7 (runbook/narrative) are
  best-effort if time remains.
- **Hold-out rehearsal:** the real 12-record hold-out file is unseen until the
  live review. A synthetic hold-out-shaped file will be generated once the
  domain schema exists (after Sprint 1), so the CLI's multi-record path is
  rehearsed today rather than run live for the first time.
- **Diagnostics:** `required_states` (consent_verified, fair_housing_check_passed,
  brand_style_applied) and violation counts are proof obligations, not part of
  the graded output. They are written to a separate `--diagnostics <file>`
  flag so `--output` stays exactly on-contract with `next_message` +
  `next_action`.

---

## Sprint 0: Scaffolding

### Before

Solution with three projects wired together and nothing else:

- `src/Agent` (class library, no business logic yet).
- `src/Agent.Cli` (console, thin shell, references `Agent`).
- `tests/Agent.Tests` (xUnit, references `Agent` only, not the CLI).
- `Directory.Build.props` at the root: `net10.0`, `Nullable enable`,
  `ImplicitUsings enable`, `LangVersion latest`, shared by all three projects
  so there is one source of truth instead of per-project drift.
- `Directory.Packages.props` for central package version management, so
  package versions live in one place as we add xUnit, the OpenAI SDK, etc.
- `.gitignore` for `bin/`, `obj/`, `.vs/`, `*.user`, and secrets.
- `README.md` pointing at `docs/DESIGN.md` and `docs/BACKLOG.md`.
- No placeholder classes added just to have a file. An empty library project
  still builds.
- Solution file: `NextBestMessageAgent.slnx`, the newer XML solution format
  supported by the .NET 9+ SDK, instead of the legacy GUID-based `.sln`.
  Chosen for Pillar 2 (cutting-edge tooling over legacy patterns); it is a
  plain, small, readable file. Fallback if the installed SDK cannot build it
  directly: regenerate a classic `.sln` referencing the same three project
  paths, no project file changes needed.

At the end of Sprint 0, three asks of Andrew (user-run, not agent-run, so the
OpenAI key and git credentials never pass through the agent):

1. `git init`, then `git remote add origin
   https://github.com/AndrewVanDelden/RealPagesInterview.git`, then push the
   scaffold, so the repo is live on GitHub for full code review from Sprint 0
   onward.
2. `dotnet user-secrets init` from `Agent.Cli`, so the project has a
   `UserSecretsId` before any composer work starts in Sprint 3.
3. `dotnet user-secrets set "OpenAI:ApiKey" "<key>"` from `Agent.Cli`, run
   directly by Andrew so the raw key is never pasted into chat or written to a
   tracked file. The key that was shared in chat earlier should be rotated
   (deleted and regenerated with All or Restricted permission, not Read only)
   before being set here, since Read-only scope will 403 on completion calls.

Acceptance: `dotnet build` succeeds across the solution; the test project
references the library; no secret or build artifact is tracked; the scaffold
is pushed and visible on GitHub; `dotnet user-secrets list` from `Agent.Cli`
shows `OpenAI:ApiKey` set.

### After

Confirmed green:

- `dotnet build` succeeded on the first try, including the `.slnx` solution
  format (no fallback to classic `.sln` needed).
- Branch strategy revised mid-sprint: Andrew decided to never commit to
  `main`. GitHub's repo-creation wizard had already put a placeholder
  `README.md` on `origin/main`, so local `main` was reset to match
  `origin/main` as-is (discarding the duplicate local scaffold commit, which
  was already safe on `dev`), and the scaffold was pushed to `origin/dev`
  instead. All sprint work continues on `dev`; `main` stays untouched as the
  protected default unless and until a PR merges into it.
- OpenAI key: the key shared earlier in chat was rotated (old one deleted).
  The new key was created scoped to only `Model capabilities > Chat
  completions (/v1/chat/completions)` = Request, everything else None,
  matching the least-privilege posture in
  [docs/DESIGN.md](docs/DESIGN.md#7-security-and-governance). Set via
  `dotnet user-secrets set "OpenAI:ApiKey" ... --project src/Agent.Cli`, run
  by Andrew; Claude never saw the raw value.

---

## Sprint 1: Domain models and ingest

### Before

- Domain records mirror the JSONL schema directly: `ProspectCase` as the
  aggregate root, with `ConsentPreferences`, `ProspectContext` /
  `ProspectProfile`, `CaseAssertions` / `CaseConstraints`, `CaseThresholds`,
  and a nullable `ExpectedOutcome` (the graded oracle, absent on real
  hold-out input the agent must not peek at for its own decisions).
  `NextMessage`, `Cta`, and `NextAction` are shared between `ExpectedOutcome`
  and the agent's own `AgentOutput`, one shape for both.
- Deserialization uses `System.Text.Json` with `JsonNamingPolicy.SnakeCaseLower`
  (added in .NET 8) for the snake_case-to-PascalCase mapping, so almost no
  property needs a `[JsonPropertyName]` attribute. Two fields are ambiguous
  under that policy's letter/digit-boundary rules (`p95_latency_ms`,
  `reply_classification_f1_min`) and got an explicit attribute rather than a
  guess. `timezone` (one word in JSON) got an attribute too, to allow the
  more descriptive `TimeZoneId` property name (HSC) instead of matching
  `Timezone`.
- Channel is a `CommunicationChannel` enum (`Sms`, `Email`, `Voice`) mapped
  via `JsonStringEnumConverter<T>` with `JsonNamingPolicy.CamelCase`.
- `IRecordReader.ReadAll` reads the file line by line, skips blank lines,
  and throws `InvalidDataException` rather than crashing on a line that
  deserializes to null.

### After

- All caught on the first `dotnet test` run except one self-inflicted
  compile error (forgot `using Xunit;`), fixed immediately.
- Found and fixed a real violation of the working agreement's BCL-collision
  rule during the refactor step: `Channel` collides with
  `System.Threading.Channels.Channel`. Renamed to `CommunicationChannel`
  while tests stayed green (refactor, not new untested implementation).
- Andrew changed the working agreement mid-sprint: Claude now runs
  `.\test.ps1` directly and reads `test-output.txt` itself (carve-out from
  "Andrew runs all dotnet/git commands," test command only), and the project
  targets 100% test coverage enforced as a build-breaking gate, not just a
  report.
- `test.ps1` now runs `dotnet test` with `coverlet.msbuild`
  (`/p:CollectCoverage=true /p:Threshold=100
  /p:ThresholdType=line,branch,method /p:ThresholdStat=total
  /p:ExcludeByAttribute=CompilerGeneratedAttribute`). Two PowerShell/MSBuild
  quirks along the way: a comma-separated `ThresholdType` value gets
  mis-split by the MSBuild CLI parser unless the comma is escaped as `%2c`;
  and multi-value `/p:` switches need to be on one line, not
  backtick-continued.
  `ExcludeByAttribute=CompilerGeneratedAttribute` excludes the
  compiler-synthesized record members (`ToString`, `Equals`, `GetHashCode`,
  `Deconstruct`, `PrintMembers`) from the coverage count, so 100% reflects
  hand-written logic, not guaranteed-correct compiler output. This is a
  judgment call Andrew can override if he'd rather have explicit record
  equality/ToString tests instead.
- The first coverage run (73.77% line / 66.66% branch / 68.88% method)
  surfaced two genuinely untested behaviors from the original implementation
  pass, both now covered: blank-line skipping and the
  deserializes-to-null-throws-`InvalidDataException` path. Both are explicit
  BACKLOG 1.2 acceptance criteria that were implemented but not
  test-driven the first time; the coverage gate caught the gap.
- Final: 5 tests, 100% line/branch/method coverage.
- Branch workflow finalized: each sprint lands on its own feature branch cut
  from `dev` (e.g. `sprint-1-domain-models-ingest`), opened as a PR into
  `dev` via `gh pr create`, not committed to `dev` directly. `main` stays
  untouched until the full epic (all sprints) is complete and merges in one
  step. Sprint 1: [PR #1](https://github.com/AndrewVanDelden/RealPagesInterview/pull/1).
- Andrew handed git/gh command execution to Claude as well ("you do it. not
  me"), on top of the earlier test-command carve-out. `dotnet user-secrets`
  remains Andrew-run, since that is the only command touching the raw
  OpenAI key.
- Post-merge code review (`/code-review` on PR #1) surfaced 7 findings, 5
  confirmed by independent verification, 2 plausible. All 7 fixed under TDD,
  each driven by a failing test where the fix changed observable behavior:
  - `ExpectedOutcome.NextMessage` was non-nullable while `AgentOutput.NextMessage`
    (the same shape) was nullable. `System.Text.Json` doesn't enforce nullable
    annotations by default, so a hold-out record exercising the documented
    suppression path (`next_message: null`) would have silently produced a
    null the type claimed couldn't happen. Root-caused with two changes:
    `AgentJsonOptions.Default` now sets `RespectNullableAnnotations = true`
    (so any genuinely-required field getting `null` throws instead of
    silently propagating), and `ExpectedOutcome.NextMessage` is now `NextMessage?`
    to honestly model the one field that's allowed to be absent.
  - `JsonlRecordReader.ReadAll` only normalized one failure mode
    (valid-JSON-deserializes-to-null) into `InvalidDataException`; malformed
    JSON leaked a raw `JsonException`, and neither message included the
    failing line number. Now both failure modes funnel through one
    `try`/`catch (JsonException)` into `InvalidDataException` with a 1-based
    line number in the message.
  - `docs/BACKLOG.md` 1.1 and `docs/DESIGN.md` both committed to "Result and
    option types in `Common/`" that were never implemented. Added
    `Agent.Common.Result<TValue>` and `Agent.Common.Option<TValue>` (minimal
    success/failure and some/none records, no consumer forced into this
    sprint's ingest code, same precedent as `IRecordReader` existing ahead of
    a second implementation).
  - `coverlet.collector` was swapped for `coverlet.msbuild` in the Sprint 1
    coverage-gate work, which silently broke `dotnet test --collect:"XPlat
    Code Coverage"` and IDE "Analyze Code Coverage" (both need the VSTest
    collector; msbuild-based coverage only activates via `test.ps1`'s
    explicit `/p:` flags). Both packages now installed side by side; they
    don't conflict, so `test.ps1`'s gate and the standard collect workflow
    both work.
  - A UTF-8 BOM had been accidentally introduced into `Agent.Cli.csproj`
    (byte-diff confirmed, likely an editor artifact from adding
    `UserSecretsId`), inconsistent with every sibling `.csproj`. Removed.
  - `JsonlRecordReader`'s private `JsonSerializerOptions` was extracted to
    `Agent.Common.AgentJsonOptions.Default` so the Sprint 5 CLI output writer
    (which needs the identical naming policy and enum converter to stay
    consistent with the input format) has a shared place to reuse it instead
    of duplicating the config later.
  - `JsonlRecordReaderTests` re-instantiated `JsonlRecordReader` in every
    fact; consolidated to one shared `static readonly` field (the reader is
    stateless, so this doesn't weaken xUnit's per-fact isolation), and
    extracted the repeated temp-file create/write/delete-in-finally pattern
    into a `WithTempFile` helper.
  - Final: 12 tests, 100% line/branch/method coverage, confirmed via
    `.\test.ps1`.
  - The 7 findings were posted as inline review comments on PR #1 first;
    fixing them was a separate, explicitly-requested follow-up step, not an
    automatic part of running the review.
  - A second, unrelated diff appeared in `docs/BACKLOG.md` during this fix
    pass: 58 lines of review notes signed "Antigravity (Gemini 3.8 Flash)"
    inserted directly into the working tree (uncommitted), matching 9
    separate review comments the same tool posted on PR #1. Confirms
    Antigravity runs locally against this checkout, not just against GitHub,
    and edits working-tree files as a side effect of reviewing. Left
    untouched and unstaged; not reviewed or authored as part of this fix
    round. Andrew is adjusting Antigravity's run settings separately.
  - Of Antigravity's 9 findings, 4 overlapped with the review above and got
    fixed incidentally (`Common/` Result/option types, `RespectNullableAnnotations`,
    missing line number, the BOM). The remaining 3 were reviewed and fixed
    as a follow-up, same TDD discipline:
    - `AgentOutput` was never constructed or deserialized anywhere in the
      codebase, so it had zero real exercise. Coverlet's `ExcludeByAttribute
      =CompilerGeneratedAttribute` setting excludes it from the coverage
      denominator entirely regardless of whether it's tested (confirmed
      empirically: it never appears in `coverage.cobertura.xml`, before or
      after adding tests for it), so the "0% coverage, bypasses the gate"
      framing doesn't quite hold. What's real is that its JSON shape (the
      naming policy, enum converter, and nullable `NextMessage` for the
      suppression case) had never been exercised in the direction Sprint 5's
      CLI will actually use it: serialization, not just deserialization.
      Added `AgentOutputTests` round-tripping it through
      `AgentJsonOptions.Default` in both directions, present and suppressed.
    - `IRecordReader.ReadAll(string filePath)` baked file I/O into an
      interface whose documented job (docs/DESIGN.md's component table) is
      "no logic beyond deserialization." Changed the signature to
      `ReadAll(TextReader reader)`; `JsonlRecordReader` now reads via
      `reader.ReadLine()` instead of `File.ReadLines(filePath)`, and opening
      the file becomes the caller's job (a `StreamReader` in tests; the
      Sprint 5 CLI will do the same). No production caller existed yet
      (`Program.cs` is still Sprint 0 scaffold), so this was the cheapest
      point to fix it. Side benefit: every synthetic-input test now uses
      `StringReader` directly instead of a temp file, so the `WithTempFile`
      helper added in the first fix round is gone entirely, not just
      simplified.
    - Several sample.jsonl fields deserialized correctly but were never
      asserted: `Cta.Link` (the only `Uri`-typed field in the schema),
      `Cta.Options`, `Input.TimeZoneId`, `Input.MoveDateTarget`,
      `Input.LastInteraction`, `Thresholds.ReplyClassificationF1Min`,
      `NextMessage.Body`/`Subject`, and `CaseConstraints.NoSensitiveDiscrimination`
      being genuinely null on the row that omits it. Added assertions for
      all of them across both sample rows. `Body`/`Subject` are asserted via
      `Assert.Contains` on an em-dash-free substring rather than full
      equality, to avoid transcribing the sample data's em dash into a
      string literal (working agreement: no em dashes in code).
    - Final: 14 tests, 100% line/branch/method coverage, confirmed via
      `.\test.ps1`.
  - A later autonomous pass caught that Antigravity's 9th finding
    ("redundant disk reads across sample parsing tests") had been claimed as
    incidentally fixed but wasn't: the `TextReader` refactor changed how
    `sample.jsonl` gets opened, not how many times. The three
    `ReadAll_ParsesSampleJsonl_*` facts each still called `ReadSample()`
    independently. Consolidated to a single `static readonly SampleCases`
    field computed once; all three facts read from it instead. With this,
    the remaining Antigravity/Gemini review threads on PR #1 no longer
    describe anything true about the code, so they were replied to and
    resolved alongside this one.

---

## Sprint 2: Deterministic decisions

### Before

- `ConsentGate.Evaluate` and `ChannelSelector.Select` share one source of
  truth for "is this channel opted in": `ConsentPreferencesExtensions.IsOptedIn`,
  a switch expression over `CommunicationChannel` in `Agent.Domain` (DRY -
  the per-channel bool lookup used to live in two places during design).
- `ChannelSelector` returns `Option<CommunicationChannel>` (from
  `Agent.Common`, already introduced during Sprint 1 code review) rather than
  a nullable enum, now that the codebase has an established option type -
  one convention for "value or absence," not two.
- `SendScheduler`: channel default hour (sms 09:00, email 10:00, voice 09:00
  as an unproven default - no sample covers voice) plus a single rollover
  rule (if today's default-hour slot has already passed relative to
  `last_interaction`, use tomorrow instead). Correction from a later review:
  the phrase "quiet-hours window" is not only DESIGN.md elaboration, it is
  in BACKLOG.md's own 2.3 acceptance bullet ("Timezone-aware, quiet-hours
  window, channel default hour"), so the original rationale here
  misattributed where the requirement came from. It genuinely is absent
  from `problem_statement.txt` and from sample.jsonl's assertions/
  thresholds, and Andrew's actual call - scoping it out in favor of the
  simpler default-hour-plus-rollover rule - stands; it independently
  satisfies all three of 2.3's stated acceptance criteria (sms → 09:00,
  email → 10:00, late-night last_interaction pushes to next day). What
  changed is only the stated reason, not the decision.
- `NextActionPlanner`: horizon = days between `move_date_target` and
  `last_interaction` (`DateOnly.DayNumber` difference), threshold defaults
  to 45 days per DESIGN.md's assumptions log. The short-horizon cadence name
  (`prospect_welcome_short_horizon`) and long-horizon follow-up interval (3
  days) are single-sample-derived constants, made configurable via
  `NextActionPlannerOptions` rather than asserted as a general rule, per the
  working agreement's "say so explicitly and make it configurable" clause.

### After

- All four components green on the first real `dotnet test` run except one
  gap the coverage gate caught: `ConsentPreferencesExtensions.IsOptedIn`'s
  switch expression triggered CS8524 (non-exhaustive) even though all three
  named `CommunicationChannel` values are handled - the compiler treats enum
  switches as open because any underlying int is castable to the enum type,
  and generates an untested fallback branch. Fixed properly, not by
  suppressing the warning: added an explicit
  `ArgumentOutOfRangeException` guard arm and a test that casts an
  out-of-range enum value to exercise it. Real coverage of real defensive
  code, not a coverage exclusion.
- Final: 27 tests total (16 new for Sprint 2 plus its 1 guard-clause test),
  100% line/branch/method coverage.
- Workflow: `gh pr merge` is blocked by Claude Code's own permission
  classifier, not by Andrew - Andrew merges each sprint's PR himself on
  GitHub going forward; Claude still creates the branch, commits, pushes,
  and opens the PR.
- Post-PR code review (`/code-review` on PR #2, plus a separate Antigravity/
  Gemini review running against the same branch) surfaced 15 findings
  across the two reviews, several overlapping. Fixed under the same TDD
  discipline as Sprint 1's review-fix round:
  - `Agent.Common.Option<TValue>.Value` and `Result<TValue>.Value`/`Error`
    returned `default(TValue)` on `None()`/`Failure()` instead of throwing.
    For a value-type `TValue` (e.g. `CommunicationChannel`, whose zero
    member is `Sms`), `Option<CommunicationChannel>.None().Value` silently
    returned `Sms` - a real channel, not an empty marker - confirmed with a
    standalone repro. Both types now throw `InvalidOperationException` on
    invalid access, matching `Nullable<T>.Value`'s convention.
  - `coverlet.collector` had been removed again in this Sprint 2 branch,
    reintroducing the exact regression Sprint 1's review fixed and merged
    into `dev`. Re-added alongside `coverlet.msbuild`. No rationale for the
    removal was ever found; it reads as branching from a working copy that
    predated the Sprint 1 fix.
  - `SendScheduler.Resolve` threw a raw, unhandled `TimeZoneNotFoundException`
    for a malformed timezone id, and `DefaultSendHour[channel]` threw a raw
    `KeyNotFoundException` for an unmapped channel - two different BCL
    exceptions for what's the same class of problem
    (`ConsentPreferencesExtensions.IsOptedIn` already throws
    `ArgumentOutOfRangeException` for the equivalent case). Extracted a
    shared `Agent.Common.TimeZones.Resolve` helper that wraps
    `FindSystemTimeZoneById` and throws `ArgumentException` with the bad id
    in the message; `DefaultSendHour` now uses `TryGetValue` and throws
    `ArgumentOutOfRangeException` for consistency with `IsOptedIn`.
  - `NextActionPlanner.Plan` had no floor check on `horizonDays`, so a
    `moveDateTarget` before `lastInteractionDate` silently produced
    `start_cadence` - identical to a legitimate near-term prospect. Now
    throws `ArgumentOutOfRangeException`.
  - `INextActionPlanner.Plan` took a bare `DateOnly lastInteractionDate`,
    with no timezone-aware derivation from `ProspectContext.LastInteraction`
    (a `DateTimeOffset`), unlike `SendScheduler.Resolve`'s established
    pattern. Changed the interface to `Plan(DateOnly moveDateTarget,
    DateTimeOffset lastInteraction, string timeZoneId)`, converting via the
    same `TimeZones.Resolve` + `TimeZoneInfo.ConvertTime` used by
    `SendScheduler`. Added a regression test with `last_interaction =
    2025-12-25T02:30:00Z` in `America/Los_Angeles`: naive UTC-based date
    extraction gives a 45-day horizon (`start_cadence`); the correct local
    date gives 46 (`follow_up_in_days`) - proving the fix actually changes
    behavior, not just the signature.
  - `NextActionPlannerOptions` accepted any integer with no validation.
    Converted from a bare positional record to one with a validating
    constructor: negative `ShortHorizonThresholdDays` or non-positive
    `LongHorizonFollowUpDays` now throw `ArgumentOutOfRangeException`. Also
    added a test proving the configurability itself works (a custom
    threshold flips a sample case's classification), which no prior test
    exercised.
  - Test coverage gaps closed: `ChannelSelector` fallback (first preference
    not consented, second one is), `ConsentGate` with empty channel
    preferences, `SendScheduler` with the `Voice` channel.
  - The "quiet-hours window" gap and its documentation misattribution (see
    correction above, Sprint 2 "Before") - decision unchanged, rationale
    corrected.
  - `SendSchedulerTests.Resolve_EmailSampleCase_ResolvesToTenAmLocal` only
    ever asserted the time-of-day, not the date, which one review flagged
    as potentially masking a bug. Investigated directly against
    `docs/DESIGN.md`'s sequence diagram and assumption #2 ("horizon is
    carried by `next_action`, not the timestamp"; `Resolve` is called with
    the raw `last_interaction`, not a follow-up-shifted date) and confirmed
    this is intended, documented scope, not a defect - composing the
    follow-up offset with the time-of-day resolution is an orchestrator
    concern for Sprint 5. Added a comment to the test explaining why, left
    the assertion as-is.
  - Final: 41 tests, 100% line/branch/method coverage, confirmed via
    `.\test.ps1`.

---

## Sprint 3: Message composition

### Before

- `IMessageComposer.ComposeAsync` returns `Result<NextMessage>` (from
  `Agent.Common`), not a bare `NextMessage` or a thrown exception. LSP
  reasoning: `TemplateMessageComposer` can never fail (always
  `Result.Success`), `OpenAiMessageComposer` can (malformed/incomplete model
  output), so both must honor the same contract for either to be a true drop-in
  swap in the orchestrator - a bare-`NextMessage` interface would force the
  OpenAI implementation to either throw (violating BACKLOG 3.3's "rejected,
  not crashed") or silently paper over failure.
- `TemplateMessageComposer`: deterministic, personalizes with first name,
  property, and interest (amenity interest if present, else city interest,
  else omitted gracefully), embeds `assertions.constraints.primary_cta`
  both literally as `Cta.Type` (for structured comparison) and as a
  human-readable phrase in the body (`book_tour` -> "book tour", for the
  literal "body contains primary CTA" acceptance wording), and always
  includes opt-out text.
- `ICompletionClient` is the one network-touching seam (`CompleteAsync`
  returns the raw model text); `OpenAiMessageComposer` deserializes that text
  as JSON into an internal `ComposedMessagePayload` and maps failures
  (invalid JSON, missing `body`/`cta_type`) to `Result.Failure` rather than
  letting an exception escape.
- `OpenAiCompletionClient` calls the Chat Completions REST API directly over
  `HttpClient` (no OpenAI SDK dependency - fewer moving parts, no version to
  pin, consistent with Pillar 2's "native features over external libraries"
  read in the direction of not adding a library where plain `HttpClient` +
  `System.Net.Http.Json` suffices). API key and model are constructor
  parameters, not read from configuration here - wiring those from
  `dotnet user-secrets` is a Sprint 5 CLI/composition-root concern (DIP: this
  class doesn't know or care where the key came from).
- Test doubles (`FakeCompletionClient`, `FakeHttpMessageHandler`) and a
  shared `SampleProspectCases.Minimal()` builder live in
  `tests/Agent.Tests/TestSupport`, not in `src/Agent` - test infrastructure
  doesn't ship in the production assembly.

### After

- All green on first real run except one coverage gap: `OpenAiMessageComposer
  .BuildUserPrompt`'s interest-selection expression (amenity interest, else
  city interest, else "no stated interest") was only ever exercised with the
  "city interest present" case, since every existing test used the same
  default sample case. Added two tests that vary the prospect's interest
  fields (amenity-only, neither) to exercise the other branches - same
  pattern as Sprint 1 and 2's coverage-gate catches, a real gap in test
  variety, not a coverage-exclusion problem.
- Final: 53 tests total, 100% line/branch/method coverage.
- Post-PR review (`/code-review` on PR #3, plus Antigravity/Gemini's parallel
  review on the same branch) surfaced 17 findings across the two reviews,
  several overlapping. Fixed under the same TDD discipline as prior sprints:
  - `TemplateMessageComposer` and `OpenAiMessageComposer` both set
    `Cta.Type` to the raw `primary_cta` value ("book_tour"), but
    `sample.jsonl`'s ground truth expects `cta.type` to be "schedule_tour"
    - a distinct output-vocabulary value, not the compliance-label string.
    Added `PrimaryCtaVocabulary.ToCtaType`, a small mapping table (the only
    known mapping, "book_tour" -> "schedule_tour", with unrecognized values
    passed through unchanged per the working agreement's "under-determined,
    make it configurable" clause). `TemplateMessageComposer` uses it
    directly; `OpenAiMessageComposer` now tells the model the required
    `cta_type` explicitly in the prompt instead of leaving it to guess.
  - `OpenAiMessageComposer.ComposeAsync` let `HttpRequestException` and
    `InvalidOperationException` from `ICompletionClient.CompleteAsync`
    propagate unhandled, defeating the whole point of `Result<NextMessage>`.
    Now wrapped in a `catch` that converts both to `Result.Failure`, with
    tests proving it for both exception types.
  - `OpenAiCompletionClient`'s outbound request used an anonymous object
    with `JsonContent.Create(requestBody)` and no explicit options, so it
    silently used `System.Net.Http.Json`'s own camelCase default instead of
    `AgentJsonOptions.Default`'s snake_case policy - correct today only
    because the anonymous type's C# property names were hand-typed already
    snake_case. Replaced with typed `OpenAiChatRequest`/
    `OpenAiChatRequestMessage`/`OpenAiResponseFormat` records (closing a
    separate Pillar 2 "Extreme Explicit Typing" finding at the same time)
    serialized with `AgentJsonOptions.Default` explicitly, so a future field
    addition renders correctly by policy, not by coincidence.
  - `EnsureSuccessStatusCode()` discarded OpenAI's actual error JSON body
    (rate-limit reason, invalid-key detail) before it could be read. Now
    reads the body on a non-success response and includes it in the thrown
    `HttpRequestException`'s message. Considered introducing a dedicated
    exception type per Antigravity's suggestion, but decided against it:
    once `OpenAiMessageComposer` catches and converts to `Result.Failure`
    at the boundary, callers of `IMessageComposer` never see the raw BCL
    exception type anyway, so a bespoke exception hierarchy would add
    surface area without a caller that needs to distinguish further.
  - `BuildUserPrompt` interpolated ingested data (first name, property,
    interest, primary CTA) directly into the LLM prompt with no boundary
    between data and instructions - a prompt-injection surface, since this
    data originates from an unseen hold-out file. Wrapped it in a labeled
    `<prospect_data>...</prospect_data>` block with an explicit instruction
    to treat its contents as data, never as directives, and echoed the same
    warning in the system prompt.
  - `TemplateMessageComposer.ComposeAsync` had no validation that
    `FirstName`, `PropertyName`, or `PrimaryCta` were non-empty - ingest
    only rejects JSON `null`, not `""`. An empty `primary_cta` would have
    produced a body like "Reply to  at Oak Ridge Apartments." while still
    returning `Result.Success`. Added a guard returning `Result.Failure`
    for any of the three being null/whitespace.
  - `OpenAiMessageComposer`'s system prompt hardcoded "opt-out instructions
    always required," ignoring `CaseConstraints.IncludeOptOutInstructions`
    (a real per-case field). `NoPiiLeak`/`NoSensitiveDiscrimination` stay a
    static always-on baseline (defensible - there's no case where a leasing
    message should discriminate), but opt-out is now a per-case directive
    in the user prompt, since a legitimately opt-out-exempt transactional
    message is realistic and the field exists specifically to vary.
  - `TemplateMessageComposer.BuildInterestPhrase` and
    `OpenAiMessageComposer.BuildUserPrompt` both independently re-derived
    the same "amenity beats city beats nothing" precedence rule - and, per
    Antigravity's finding, that precedence silently dropped city interest
    whenever amenity interest was also present, losing real personalization
    context. Added `ProspectProfile.HasAmenityInterest`/`HasCityInterest`
    (trivial, testable presence checks) and rewrote both composers to
    mention amenity interest AND city interest whenever each is present,
    instead of picking one exclusively. This removes both the duplication
    (no more shared either/or decision to keep in sync) and the
    drops-city-interest bug in one fix.
  - Antigravity also flagged `TemplateMessageComposer`'s `Cta.Options`/
    `Cta.Link` always being `null`, differing from `sample.jsonl`'s CTA
    shapes (tour time slots, a booking link). Investigated and left
    unchanged: no field on `ProspectCase`/`ProspectContext`/
    `ProspectProfile` carries tour-slot or booking-URL data anywhere -
    those sample values are the LLM's own invented/business-supplied
    content. Fabricating them in the deterministic template composer would
    contradict the system prompt's own "never invent availability" rule
    applied to the wrong composer; the honest `null` is correct until a
    real domain field exists to populate them.
  - `docs/BACKLOG.md`'s Sprint 2.3 bullet still said "quiet-hours window"
    with no pointer to the scope-out decision recorded in
    `docs/CODE_REVIEW.md` and `docs/DESIGN.md`. Added a one-line
    cross-reference rather than leaving the acceptance bullet unexplained.
  - Final: 71 tests, 100% line/branch/method coverage, confirmed via
    `.\test.ps1`.

---

## Sprint 4: Safety and fair-housing validator

### Before

- `ISafetyValidator.Validate(NextMessage, CaseConstraints)` returns
  `ValidationResult(Violations, FairHousingCheckPassed)`. Assumption made
  explicit (BACKLOG bundles opt-out/PII/steering under one validator but
  only names one required-state, `fair_housing_check_passed`, for all
  three): `FairHousingCheckPassed = Violations.Count == 0`, i.e. it means
  the check came back clean, not merely "the check ran" (that distinction
  matters: `ConsentGate.ConsentVerified` means the check ran regardless of
  outcome, which is a deliberately different semantic for a deliberately
  different question).
- Three checks: opt-out presence (keyword match, gated on
  `IncludeOptOutInstructions`), PII leak (SSN pattern + long-digit-run regex,
  gated on `NoPiiLeak`), protected-class/steering language (keyword
  deny-list). All three are heuristics, not a comprehensive fair-housing
  compliance system - explicitly noted as a scope limitation in the code
  itself and in `docs/CODE_REVIEW.md`, not silently presented as complete.
- `ValidatingMessageComposer` is a decorator over `IMessageComposer` (DIP/OCP
  - wraps any composer, including a fake, without modifying it): try the
  inner composer, validate; if clean, return it; otherwise retry the inner
  composer exactly once; if still unclean (or the composer itself fails),
  hard-stop at a separate fallback composer. Bounded by construction (a
  `for` loop capped at 2 attempts), not by a manually-tracked counter that
  could be gotten wrong.

### After

- Regexes for the PII checks used `[GeneratedRegex]` (.NET 7+ source
  generator - Pillar 2's cutting-edge-over-legacy call over `new
  Regex(pattern, RegexOptions.Compiled)`), which surfaced a real coverage
  tooling gap: the generator emits a whole state-machine file
  (`RegexGenerator.g.cs`) that `ExcludeByAttribute=CompilerGeneratedAttribute`
  does not exclude, and its internal branches for regex edge cases our two
  simple patterns never trigger counted against the 100% gate (dropped to
  84% line / 64% branch on the first run). Standard fix, not a metric dodge:
  added `/p:ExcludeByFile="**/*.g.cs"` to `test.ps1`, the same exclusion
  principle already applied to compiler-generated record members, just a
  different coverlet mechanism for source-generator output.
- Final: 83 tests total, 100% line/branch/method coverage.
- Post-PR review (`/code-review` on PR #4, plus Antigravity/Gemini's parallel
  review on the same branch) surfaced 17 findings across the two reviews,
  several overlapping. Fixed under the same TDD discipline as prior sprints:
  - `ValidatingMessageComposer` validated every retry attempt but returned
    `fallbackComposer`'s output directly with no validation at all - the one
    exit path Sprint 4's stated goal ("nothing unsafe leaves the agent")
    most needed checked. Now validates the fallback too; if even the
    fallback fails, returns `Result.Failure` rather than shipping
    unvalidated content.
  - `SafetyValidator`'s protected-class/steering matching was unanchored
    substring search, so legitimate data collided with short deny-list
    terms: a prospect interested in "Colorado Springs" tripped "color", a
    prospect named "Christian" tripped the term itself. Traced end to end
    and confirmed both are realistic, not contrived. Switched steering-term
    matching to word-boundary regex, which fixes the substring class
    (Colorado/color, terrace/race, colorful/color) - it does not and cannot
    fix the homonym class (a common first name that's also a religion name),
    which is an inherent limit of keyword matching, not a bug; the
    validate-the-fallback-too fix above means a false positive now fails
    safe (`Result.Failure`) instead of either shipping unsafe content or
    looping forever.
  - `OptOutPhrases` included the bare word "stop", so "bus stop" satisfied
    the opt-out requirement with no real opt-out language present - and
    word-boundary anchoring alone doesn't fix this, since "stop" in "bus
    stop" is already a complete word. Replaced the bare word with the
    specific phrases the composers actually generate ("reply stop", "text
    stop", "opt out", "opt-out", "unsubscribe").
  - `LongDigitRunPattern` (`\b\d{13,19}\b`) only matched an unbroken digit
    run, so a formatted card number ("4111-1111-1111-1111", the common
    real-world format) evaded detection since each group is only 4 digits.
    Rewrote to `\b\d(?:[- ]?\d){12,18}\b` - same 13-19 total digit
    threshold, but tolerant of space/dash grouping.
  - `Validate` only ever checked `message.Body`; `message.Subject` (real
    content for Email messages, LLM-generated with no more sanitization
    than Body) was never checked at all. Now checks Subject and Body
    together, with tests for a clean body plus a steering/PII-leaking
    subject.
  - The compose-validate retry loop re-invoked the inner composer with
    identical input on retry, no feedback about what was wrong - pointless
    for `TemplateMessageComposer` (deterministic; a retry can never change
    its output) and a blind re-roll for `OpenAiMessageComposer`. Added an
    optional `priorViolations` parameter to `IMessageComposer.ComposeAsync`;
    `ValidatingMessageComposer` now passes the first attempt's violations
    into the second attempt, and `OpenAiMessageComposer` appends them to the
    retry prompt as explicit correction feedback. `TemplateMessageComposer`
    accepts and ignores the parameter (documented why).
  - `ContainsAny`/`FindFirst` were near-duplicate iteration over the same
    predicate. Collapsed to one `FindFirst`, with `ContainsAny` expressed
    in terms of it.
  - `docs/CODE_REVIEW.md`'s "Known, deliberate scope decisions" section
    was missing the Sprint 4 entry that `SafetyValidator.cs`'s own comment
    (and TalkingPoints.md's Sprint 4 "Before" section) both claimed existed
    there - a broken cross-reference from two directions. Added the entry,
    including the `NoSensitiveDiscrimination`-is-intentionally-unread
    rationale.
  - `Agent.Safety.ValidationResult` collided by name with
    `System.ComponentModel.DataAnnotations.ValidationResult`, latent today
    (nothing imports that namespace) but the same category of issue that
    got `Channel` renamed to `CommunicationChannel` in Sprint 1. Renamed to
    `SafetyValidationResult`.
  - Final: 95 tests, 100% line/branch/method coverage, confirmed via
    `.\test.ps1`.

---

## Sprint 5: Orchestrator and CLI

### Before

- `LeasingMessageAgent` (`IMessageAgent`) holds no business rules of its own
  (DESIGN.md section 5) - it only sequences the already-tested components:
  consent gate, channel selector, composer, scheduler, planner, and a final
  safety re-validation for diagnostics. `next_action` is planned on both the
  suppressed and contactable paths, matching the pipeline diagram (both
  branches feed into the planner).
- Deliberately does not re-guard `Option<T>.Value` / `Result<T>.Value` with
  its own null/failure checks before using them: both already throw a clear
  `InvalidOperationException` on the "should never happen" case. Duplicating
  that guard in the orchestrator would be dead code no honest test could
  reach (SafetyGate and ChannelSelector share the same `IsOptedIn` source of
  truth, so they cannot disagree in practice) - so `.Value` is used directly,
  and the one failure path that _can_ genuinely occur (composer produces no
  safe message even through the fallback) is exercised via a real case with
  an empty first name, not a fake.
- `FairHousingCheckPassed`/`SafetyViolationCount` in diagnostics come from
  the orchestrator re-validating the final message itself, even though
  `ValidatingMessageComposer` already validated internally. Small
  duplicated, pure, no-I/O check, traded deliberately for not coupling the
  orchestrator's diagnostics to whatever composer implementation happens to
  be injected.
- The CLI (`Agent.Cli/Program.cs`) is treated as composition-root wiring, not
  business logic: `tests/Agent.Tests` only ever referenced `Agent`, never
  `Agent.Cli` (a Sprint 0 decision), and BACKLOG 5.2's own acceptance
  criterion is a manual run (`dotnet run -- --input ... --output ...`), not
  a unit test - unlike every other sprint. So the CLI isn't unit-tested or
  subject to the coverage gate; it's verified by actually running it.
  `--composer` defaults to `template` (no network dependency); `--composer
  openai` reads `OpenAI:ApiKey`/`OpenAI:Model` via
  `Microsoft.Extensions.Configuration` (user-secrets + environment
  variables) - added now since it's the official mechanism for reading
  `dotnet user-secrets`, not a legacy pattern being avoided. The OpenAI path
  always falls back to `TemplateMessageComposer` on unsafe/failed output,
  never to another OpenAI call, so "safe fallback" stays actually safe. The
  `--diagnostics <file>` flag from the kickoff decisions is implemented too.

### After

- Orchestrator: all tests green on the first run, 100% coverage, no
  surprises - the two sample-based acceptance tests
  (`RunAsync_Sample1_ProducesSmsAndStartCadence` /
  `RunAsync_Sample2_ProducesEmailAndFollowUpInDays`) use the real
  components end-to-end against the actual `sample.jsonl`, not fakes,
  directly exercising BACKLOG 5.1's literal acceptance criterion.
- Process error, not a design one: wrote all of Sprint 5 directly on `dev`
  instead of branching first (forgot the branch-off step after syncing).
  Caught before anything was committed - moved the uncommitted work to
  `sprint-5-orchestrator-cli` with `git checkout -b`, which carries
  uncommitted changes to the new branch and leaves `dev` untouched. No
  commits were lost or needed to be undone.
- Ran the actual CLI acceptance check from BACKLOG 5.2: `dotnet run
  --project src/Agent.Cli -- --input sample.jsonl --output out.jsonl
  --diagnostics diagnostics.jsonl`. Output matched both samples' expected
  `next_message.channel` and `next_action.type` exactly (sms/start_cadence,
  email/follow_up_in_days with value 3), and diagnostics showed clean
  `consent_verified`/`fair_housing_check_passed`/`brand_style_applied` for
  both records with zero safety violations. This is the MVP finish line per
  the kickoff time-budget decision. Verification files (`out.jsonl`,
  `diagnostics.jsonl`) were deleted after inspection, not committed.

---

## Sprint 6: Eval harness

### Before

`IEvaluator`/`Evaluator` runs the agent over a labeled file (one with
`expected` populated) and produces a `Scorecard` of per-record `RecordScore`s
- proving the thresholds by actually executing and timing the pipeline, not
asserting it behaves a given way. Two deliberate departures from DESIGN.md
section 6's original wording, made explicit rather than silently
implemented differently:

- **`no_pii_leak` and `safety_violations == 0` collapse into one field**
  (`SafetyViolationsWithinBudget`). `SafetyValidator` itself returns
  per-category violation messages, but `LeasingMessageAgent` collapses
  them into a single count (`AgentDiagnostics.SafetyViolationCount`)
  before they reach the evaluator - scoring PII and safety-violation-count
  as two separate signals isn't supported by the diagnostics contract this
  evaluator actually receives.
- **The "horizon cue" personalization token is dropped.** First name,
  property, and interest are each a concrete, checkable string. "Horizon
  cue" names no specific pattern - scoring it would mean inventing a rule
  with no evidence behind it, the exact thing the working agreement's
  "separate facts from inferences" rule warns against.

`Evaluator.ContainsOptOutPhrase` reuses `SafetyValidator.OptOutPhrases`
directly (changed from `private` to `internal`) rather than maintaining a
second phrase list that could drift from what the validator actually
enforces. Latency is measured with a real `Stopwatch` around each
`agent.RunAsync` call, not injected/faked - tests that need a slow run use
an artificial `Task.Delay` in a fake `IMessageAgent` against a
deliberately tiny threshold, avoiding both real DI ceremony for a leaf
timing concern and flaky sleep-based assertions.

`ScorecardFormatter` is a plain aligned-text table (`Task ID | Channel |
Action | ...`), not a table library - a handful of columns read by a human
during the live review doesn't need a dependency.

CLI: new `--eval-report <file>` flag. When present, prints the scorecard to
the console and writes it to the file, scored from the same results already
captured during the main `--output` pass - `Evaluator` takes precomputed
`ScoredRun`s (case, result, latency) rather than calling `agent.RunAsync`
itself, so the report describes exactly what was persisted, not a second,
possibly different sample.

### After

- Two self-inflicted test bugs during the initial run, neither an
  implementation bug: (1) `BaselineExpected(message: null)`'s `??`
  couldn't distinguish "explicitly want null" from "not specified, use
  default," silently defaulting instead of producing the suppressed
  `ExpectedOutcome` the test needed - fixed by constructing that one
  `ExpectedOutcome` directly instead of through the ambiguous helper; (2)
  a personalization test asserted a perfect 1.0 score without accounting
  for `SampleProspectCases.Minimal()`'s default city interest, which the
  test's message body didn't mention - fixed the body text.
- The coverage gate caught a real gap in test variety (the same pattern as
  every prior sprint): `ComputePersonalizationScore`'s `CityInterest is
  { Length: > 0 }` pattern had only ever been exercised with a present,
  non-empty city interest - no test covered "no interest stated at all."
  Added one.
- Extracted `RealAgentFactory` (the real-component wiring `BuildRealAgent`
  / `ReadSampleCases`) out of `LeasingMessageAgentTests` into
  `TestSupport`, since `EvaluatorTests`' own sample.jsonl integration test
  needed the identical setup - duplicating it across two test classes
  would have been a real DRY violation, not a stylistic one.
- Verified against the actual `sample.jsonl` file end-to-end (not just
  unit tests): both records pass every column - channel, action, opt-out,
  CTA, safety, personalization (1.00 on both), latency - exceeding
  BACKLOG 6.1's narrower literal acceptance criterion (channel, action,
  personalization only).
- Final: 147 tests in `Agent.Tests`, 11 in `Agent.Cli.Tests`, 100%
  line/branch/method coverage on both.

### Post-review fix round

A code review (own findings plus Antigravity/Gemini's) surfaced several
real gaps, addressed together since fixing the most substantive one
(the double-run) reshaped how the others were fixed:

- **`Evaluator` no longer drives its own `agent.RunAsync` loop.** It now
  takes precomputed `ScoredRun`s (case, result, latency) captured once by
  `CliRunner` during the main batch pass, and `Evaluate` is a synchronous,
  pure scoring function. This closes three findings at once: the eval
  report can no longer describe a different (non-deterministic) sample
  than what was persisted to `--output`; a `--composer openai` run no
  longer pays for the batch twice; and per-record isolation is no longer
  Evaluator's problem to solve, since it never touches the agent at all.
- **A case missing its labeled `expected` outcome is now an unscoreable
  row, not a crash.** `RecordScore.Unscoreable` records the reason;
  `CliRunner` logs it to stderr for visibility but no longer lets it
  override the main pass's exit code - a labeling gap in the optional eval
  rehearsal and a broken production batch are no longer indistinguishable
  from the exit code alone.
- **`ComputePersonalizationScore`'s `AmenityInterest`/`CityInterest` check
  changed from `if/else if` to two independent `if`s** (and now reads
  through `ProspectProfile.Amenities`/`City`, the same normalized
  properties the composers already use) - a prospect with both no longer
  has city interest silently dropped from scoring.
- **`ContainsOptOutPhrase` now checks Subject+Body**, mirroring
  `SafetyValidator.Validate`'s own search text, instead of Body alone.
- **`ScorecardFormatter` now pads columns to a computed width** so rows
  align regardless of `TaskId` length, and renders unscoreable rows with
  their reason instead of blank columns.
- **`SafetyValidator.OptOutPhrases` is now `IReadOnlyList<string>`**
  instead of a mutable `string[]`, per Pillar 2 (immutability).
- Not changed: the per-record latency check against `P95LatencyMs`
  remains a per-record ceiling, not a computed percentile - that's
  DESIGN.md's own long-standing wording ("wall-clock per record against
  p95_latency_ms"), inherited from the problem's own threshold field name,
  and a true percentile isn't a meaningful calculation over a 2-12 record
  batch anyway.

---

## Sprint 7: Live hold-out runbook and narrative

### Before

BACKLOG's original framing of this sprint ("be ready to run 12 hold-outs
live") assumed the runbook would be written *before* the live review, as
rehearsal. That didn't happen the way planned - by the time this sprint was
picked up, the interview itself was already over and Andrew supplied the
actual 12-record hold-out file that had been used. So this sprint became
what BACKLOG's acceptance criteria describe either way: running the CLI
against the real hold-out and writing down what actually happened, just
after the fact instead of before it. Every command below was actually run,
in this order, against the actual file.

### After

**The exact commands run**, against the real 12-record file (saved for this
analysis at a session-scratchpad path, not committed to the repo - it is
Andrew's interview material, not project source):

```
dotnet run --project src/Agent.Cli -- --input <holdout-12>.jsonl --output out.json
```

First run, before any fix: **unhandled crash**, exit code 127, before a
single output line was written:

```
Unhandled exception. System.ArgumentNullException: Value cannot be null. (Parameter 'key')
   at System.Collections.Generic.Dictionary`2.FindValue(TKey key)
   at System.Collections.Generic.Dictionary`2.TryGetValue(TKey key, TValue& value)
   at Agent.Composition.PrimaryCtaVocabulary.ToCtaType(String primaryCta) in ...\PrimaryCtaVocabulary.cs:line 11
   at Agent.Evaluation.Evaluator.Score(ScoredRun run) in ...\Evaluator.cs:line 53
   ...
```

**Root cause:** `PrimaryCtaVocabulary.ToCtaType(string primaryCta)` took a
non-nullable `string`, but two records in the actual hold-out
(`prospect_consent_block_sms_fallback_email`,
`resident_renewal_details_branch_email`) have no `primary_cta` in their
`constraints` at all. A missing JSON property deserializes
`CaseConstraints.PrimaryCta` to C# `null` (not a thrown exception - the
same "missing, not explicitly null" class of gap already documented for
`move_date_target` after the earlier synthetic-file rehearsal), and
`Dictionary.TryGetValue(null)` throws. `Evaluator.Score` called this with
no null-guard; `OpenAiMessageComposer.ComposeAsync` had the identical
unguarded call (would have crashed identically under `--composer openai`);
only `TemplateMessageComposer` happened to be protected, by an unrelated
upfront `IsNullOrWhiteSpace` check that existed for a different reason, not
because anyone had reasoned about `PrimaryCtaVocabulary` specifically.

**A second, related gap found in the same conversation, before the rerun:**
the eval-report code path (`Scorecard scorecard = evaluator.Evaluate(scoredRuns);`
in `CliRunner`) had *no catch block at all* - unlike the main batch loop,
which isolates one bad record from the rest. That is the actual reason a
single record's bug crashed the entire run instead of degrading one row of
the scorecard.

**Fixes, root cause not per-caller patches:**

- `PrimaryCtaVocabulary.ToCtaType` now takes and returns `string?`: null in,
  null out. Every caller updated to treat "no required CTA type" as its own
  valid state, not an error - `Evaluator`'s `PrimaryCtaPresent` check is
  trivially true when there is no required type to check against (same
  pattern as `OptOutPresent` when opt-out isn't required);
  `OpenAiMessageComposer` skips constraining the response schema's
  `cta_type` enum and skips the post-hoc equality check when there is
  nothing to constrain it to.
- `Evaluator.Evaluate` now wraps each record's `Score(run)` call in its own
  `try`/`catch`, converting an unexpected exception into
  `RecordScore.Unscoreable` for that one row instead of losing the whole
  batch - the same per-record-isolation principle `CliRunner`'s main loop
  already used, now applied consistently to the scorer too.
- Both catch sites now capture `ex.GetType().Name` alongside `ex.Message`.
  A bare `ex.Message` for this exact bug read `"Value cannot be null.
  (Parameter 'key')"` - useless without the type. The irony this bug
  exposed directly: the *unhandled* crash's default .NET stack trace (file,
  line, full call chain) was more informative than anything our own
  deliberate error handling captured anywhere in the codebase. See Sprint 8
  for the full audit this observation triggered.
- Added tests for all three changed branches (`PrimaryCtaVocabulary`'s null
  path, `OpenAiMessageComposer`'s skip-the-schema-constraint path, and the
  new `Evaluator` catch block, the last one triggered with a deliberately
  malformed `NextMessage.Body` forced null via `!` despite its non-nullable
  static type - the same "type says non-null, real data disagrees" pattern
  that caused the original bug, used deliberately as the test's mechanism).

**Second run, after the fix**, exit code 0, no crash:

```
dotnet run --project src/Agent.Cli -- --input <holdout-12>.jsonl --output out.json --diagnostics diagnostics.json --eval-report eval.txt
```

Result: **2 of 12 records pass the eval harness.** Every failure traces to
one of two already-known, already-documented schema-coverage gaps - not new
bugs, and not chased further here since the interview this data is from is
already complete:

1. **Missing `move_date_target`** (5 records: cancellation, both
   residents without a stated move date, loyalty engagement, Spanish
   locale) defaults to `0001-01-01` (`DateOnly`/`DateTimeOffset` are
   non-nullable value types - a missing property doesn't throw, it
   defaults), producing `start_cadence` where the labeled data expects
   `follow_up_in_days`.
2. **Missing `primary_cta`** (the same 2 records that used to crash) is now
   handled safely by `TemplateMessageComposer`'s existing
   `IsNullOrWhiteSpace` guard - but that guard treats "no CTA stated" as a
   *missing required field* and suppresses the message entirely, rather
   than composing a message with no specific CTA phrase. That is a
   legitimate, still-open design question (not resolved here): should "no
   `primary_cta`" mean "nothing to say" or "say something, just not tied to
   one specific CTA"? The real data suggests the latter (both of those
   records have a real, expected email in the labeled data), but changing
   `TemplateMessageComposer`'s behavior now, after the graded review, would
   be design work done in the wrong sprint for the wrong reason.
3. **A closed `next_action` vocabulary.** `start_cadence` and
   `follow_up_in_days` were the only two shapes the two original labeled
   samples showed. The real hold-out's other personas and lifecycle stages
   (no-show re-engagement, lease renewal, resident welcome, an intent-branch
   flow, an e-sign flow) need `reset_cadence`, `schedule_sms_reminder`,
   `branch_on_intent`, `no_op`, `start_esign_flow` - a materially larger
   action vocabulary than a two-sample take-home could reasonably infer.
   This is the honest boundary of what "generalizes from 2 examples" can
   promise, not an implementation defect.

One record (`resident_opt_out_respected`) shows as unscoreable rather than
scored right or wrong: its labeled `expected.next_message.channel` is
`"none"`, which is not a value our `CommunicationChannel` enum has - the
`LenientExpectedOutcomeConverter` from the earlier ingestion-crash fix (see
the hardening entry above) correctly parsed the rest of that record and set
`Expected = null` for just the unparseable oracle, exactly as designed. Its
*actual* output (message suppressed, `next_action: no_op`-equivalent
suppression) is arguably correct given `respect_consent: true` and all
three consent flags false - it simply cannot be scored against a `"none"`
channel value with no corresponding enum member, which is a distinct,
smaller gap from the two above (the enum would need a `None`/`Suppressed`
member to represent "deliberately no channel" as a real, nameable value
rather than only "absent").

No code changes were made in this sprint beyond the crash fix - the
schema-coverage gaps are recorded as findings, not treated as bugs to fix
under time pressure after the actual grading already happened.

---

## Post-MVP hardening: lenient parsing of the `expected` oracle

### Before

Found while rehearsing the CLI with a hand-built 11-record synthetic
hold-out-shaped file (the "hold-out rehearsal" from the kickoff decisions):
`dotnet run --composer openai` crashed immediately on ingestion, before any
composer ran, with a `JsonException` on record 10's `expected.next_message
.channel: "none"` - a value our `CommunicationChannel` enum doesn't have
(`Sms`/`Email`/`Voice` only).

Root cause, not just the symptom: `JsonlRecordReader` deserialized the whole
line - including `expected` - as one strict object. `expected` is only the
scoring oracle (DESIGN.md section 2); the agent's own decision pipeline
never reads `ProspectCase.Expected`. So a single record's `expected` having
any shape outside our exact schema (an unrecognized channel, a novel
`next_action` shape neither of the two labeled samples showed) took down
ingestion for the entire file, discarding every other record too - a much
worse failure mode than "scores one record wrong," and a real risk against
the actual 12-record hold-out, which is very unlikely to be limited to the
two `next_action` shapes ("start_cadence", "follow_up_in_days") the labeled
samples happened to show.

Fix: `JsonlRecordReader` now parses each line in two passes via
`JsonNode` - strip `expected` out before the strict pass (so `TaskId`,
`Consent`, `ChannelPreferences`, `Input`, `Assertions`, `Thresholds` still
parse strictly and loudly, since the agent genuinely depends on them), then
attempt `expected` separately and fall back to `null` on any parse failure,
rather than letting it fail the whole record.

### After

- Refactoring the parse into two passes turned a previously-reachable
  `?? throw` guard (`ReadAll_ThrowsInvalidDataException_WithLineNumber_WhenLineDeserializesToNull`
  covers the *outer* "whole line is JSON null" case now) into dead code on
  the *inner* pass: once `rootNode` is confirmed non-null, a non-null
  `JsonNode` cannot deserialize to a null `ProspectCase`, so that second
  guard could never be reached by any honest test. Removed it (null-forgiving
  operator with a comment explaining why) rather than write a test to poke
  at unreachable code.
- `JsonNode.Parse`'s syntax-error exception is `JsonReaderException` (a
  `JsonException` subclass), where `JsonSerializer.Deserialize` had thrown
  the base `JsonException` directly - an existing test asserted the exact
  concrete type (`Assert.IsType<JsonException>`). Fixed the assertion to
  `Assert.IsAssignableFrom<JsonException>`, since that's what the code
  actually guarantees (catches `JsonException` and any subclass) and was
  always the more correct assertion - not something to work around.
- Verified against the actual synthetic file that originally crashed: all
  11 records now process without error (`dotnet run` exits 0). Two further,
  separate findings from that same file, deliberately not fixed here since
  they're schema-coverage questions rather than parsing bugs: (1) records
  missing `move_date_target` (new lifecycle stages this file introduced -
  no-show, cancellation, renewal - that don't naturally have a "move date")
  silently default `send_at` to `0001-01-01`, since `DateOnly`/`DateTimeOffset`
  are non-nullable value types that don't fail on a missing JSON property
  the way a missing required reference-type field would; (2) records missing
  `primary_cta` get a null `PrimaryCta`, which `TemplateMessageComposer`
  correctly refuses to compose from, but the orchestrator's failure handling
  folds that into the same "suppressed" output/diagnostics shape as a
  genuine no-consent case, conflating two different situations.
- Final: 107 tests, 100% line/branch/method coverage.

---

## Post-MVP hardening: `OpenAiMessageComposer.cs` code-quality review

### Before

A code-quality pass (framed as "how would a senior C/C++ systems engineer
review this file") raised nine points against `OpenAiMessageComposer.cs`.
Assessed each on its actual merits rather than accepting or dismissing the
critique wholesale - several were technically correct but misapplied
performance concerns for I/O-bound code (a per-record prompt-build ahead of
a multi-hundred-millisecond OpenAI call is not a hot loop); a couple were
genuinely valid and worth fixing regardless of who raised them:

1. **CTA-type validation gap (the most substantive finding).** The prompt
   tells the model `required_cta_type: schedule_tour`, but nothing checked
   the model's response actually matched - the only validation was
   "`cta_type` is non-empty." A plausible-looking but wrong CTA
   (`call_now` when `schedule_tour` was required) passed silently. This
   was a real unenforced constraint, not a style nitpick.
2. **Prose-only output format ("prompt-begging").** `response_format:
   json_object` only guarantees syntactically valid JSON, not that it
   matches our shape - shape compliance rested entirely on the model
   choosing to follow English instructions. OpenAI's Structured Outputs
   (`response_format: json_schema`, `strict: true`, GA since August 2024)
   enforces the shape at the API level via constrained decoding.
3. **Legacy string concatenation for `SystemPrompt`.** 11 lines of `+`
   concatenation with escaped quotes, when C# 11 raw string literals
   (`"""..."""`) exist for exactly this and are already used elsewhere in
   this codebase - directly inconsistent with our own Pillar 2 ("cutting-
   edge over legacy patterns").
4. **Null-forgiving operator (`!`) instead of pattern matching.** `if
   (profile.HasAmenityInterest) { ...AmenityInterest! }` relies on a human
   keeping `HasAmenityInterest`'s definition in sync with actual
   nullability, with no compiler enforcement - the pointer-dereference
   analogy is apt, since `!` is explicitly "trust me" syntax. `if
   (profile.AmenityInterest is { Count: > 0 } amenities)` gets the same
   result with the compiler proving safety instead of a human promising it.

Six other points (heap allocations from string building, `Deserialize<T>
(string)`'s internal UTF-16-to-UTF-8 transcode, exceptions for JSON
validation, `Enum.ToString()` cost, `var` vs. explicit types) were judged
technically accurate but not worth acting on given this workload - see the
interview-prep discussion for the full per-point reasoning. The
stringly-typed-CTA point (raw `string CtaType`, no enum) was folded into
fix #1 above: the real gap wasn't the type, it was the missing check.

### After

- Changed `ICompletionClient.CompleteAsync` to accept an optional
  `responseJsonSchema` (raw JSON Schema text; null = plain `json_object`
  mode) - the schema is owned by `OpenAiMessageComposer` (it knows
  `ComposedMessagePayload`'s shape), while `OpenAiCompletionClient` owns
  wrapping it in OpenAI's specific request envelope (`name`/`strict`), each
  staying responsible for what it actually knows.
- `OpenAiMessageComposer.ComposeAsync` now computes `requiredCtaType` once
  (used both for the prompt and for validating the response) and returns
  `Result.Failure` when the model's `cta_type` doesn't match it.
- Fixing the null-forgiving pattern in `OpenAiMessageComposer` surfaced the
  identical pattern in `TemplateMessageComposer.BuildInterestPhrase` -
  fixed both for consistency, not just the one flagged. That in turn made
  `ProspectProfile.HasAmenityInterest`/`HasCityInterest` genuinely dead code
  (nothing called them anymore) - removed them and their dedicated test
  file rather than leave unused public API behind.
- One test assertion (`DoesNotContain("\"json_schema\"", ...)` for the
  no-schema case) was wrong, not the production code: `OpenAiResponseFormat
  .JsonSchema` still serializes as `"json_schema":null` when absent (a
  harmless key OpenAI ignores when `type` is `json_object`) - fixed the
  assertion to check what's actually guaranteed (`"type":"json_object"`
  and `"json_schema":null`), not a substring that was never a real
  contract.
- Verified end-to-end against `sample.jsonl` with the template composer
  after all changes: still produces the correct sms/`start_cadence` and
  email/`follow_up_in_days` outputs.
- Final: 109 tests, 100% line/branch/method coverage across both `Agent
  .Tests` and `Agent.Cli.Tests`.

---

## Post-MVP hardening: readable JSON output

### Before

Andrew's reaction to the actual CLI output file: unreadable. `System.Text
.Json`'s default encoder escapes ordinary punctuation and every non-ASCII
character to `\uXXXX` sequences as an HTML/XSS precaution - an apostrophe
in a composed message became `'`, an accented name like "Lucía" became
"Lucía". None of our JSON is ever embedded in a web page; it is a
JSONL file read by our own reader and, just as importantly, by a human
reviewing it. The precaution had no benefit here and made every composed
message harder to read than the actual text the LLM or template wrote.

This is a readability fix, not a format change: the output stays valid
JSONL (one JSON object per line, per BACKLOG 5.2's contract) - only the
*content* of the strings changes, from escaped to literal characters.

### After

- `AgentJsonOptions.Default` (the single shared options object used by
  ingestion, output serialization, and the OpenAI request/response bodies)
  now sets `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. Because
  every JSON touchpoint in the codebase already went through this one
  shared options object (a Sprint 1 decision), fixing it in one place fixed
  it everywhere - ingestion, CLI output, and the OpenAI wire format all read
  cleanly now, not just the output file.
- Verified against the actual CLI output: the body text now reads
  `you're looking in Richardson, TX` instead of the previous escaped
  form (a backslash-u sequence in place of the apostrophe).
- Final: 111 tests, 100% line/branch/method coverage.

---

## Post-MVP hardening: output format changed from JSONL to a JSON array

### Before

Andrew's principle for resolving this kind of question, stated directly:
if it's in `problem_statement.txt`, it's law and gets done exactly as
stated; if it only appears in our own planning documents (`BACKLOG.md`,
`DESIGN.md`), it can be amended when something better is warranted.
Re-checked `problem_statement.txt` against that test: it says "You are
given a JSONL file" (input only) and never once constrains the *output*
format. "Read JSONL, write JSONL" in BACKLOG.md 5.2 was purely our own
invention, not a requirement - amendable, and Andrew confirmed amending it
directly ("backlog can be ammended").

Changed the CLI's `--output`/`--diagnostics` files from compact
line-delimited JSONL to a single indented JSON array. Input parsing stays
strict JSONL, unchanged - that half of the format *is* stated in the actual
ask.

### After

- `JsonlRecordWriter<T>` (line-per-record) replaced with
  `JsonArrayRecordWriter<T>` (one indented JSON array via
  `JsonSerializer.Serialize(IEnumerable<T>, ...)` with `WriteIndented =
  true` - the array wrapping is free, System.Text.Json serializes any
  `IEnumerable<T>` as a JSON array natively). Empty input now correctly
  produces `[]`, a valid JSON document, rather than an empty/zero-byte file.
- `CliRunner` restructured: results now accumulate into `List<AgentOutput>`
  / `List<TaskDiagnostics>` during the per-record loop (per-record error
  isolation unchanged - one bad record still can't discard the others) and
  the whole array is written once after the loop, instead of appending one
  line per record as it completed. Traded away: true crash resilience (a
  genuine unhandled crash mid-loop now loses the whole batch instead of
  whatever had already been flushed) for a single well-formed JSON
  document - deliberate, since a real crash is already an "something is
  broken" scenario outside the intended contract either way.
  `Agent.Cli.Tests` assertions that counted output lines (`File
  .ReadAllLinesAsync(...).Length`) were rewritten to parse the file as JSON
  and check `GetArrayLength()` / element properties instead - counting
  lines was always really an assumption about JSONL, not what the tests
  were actually trying to verify (record count, correct shape).
  `docs/BACKLOG.md`, `README.md` amended to match (`out.jsonl` -> `out.json`
  in examples), consistent with the "can be amended" ruling.
- Final: 112 tests, 100% line/branch/method coverage across `Agent.Tests`
  and `Agent.Cli.Tests`.

---

## PR #9 review response

Andrew's own review (via the Antigravity/Gemini reviewer, per
[docs/CODE_REVIEW.md](docs/CODE_REVIEW.md)) on the escaping/JSON-array PR
raised seven points. Triaged rather than blanket-applied:

**Fixed:**

1. **Fail-fast regression (a real bug the refactor introduced).**
   Accumulating results and writing once at the end had moved
   `new StreamWriter(outputPath)` to *after* the whole batch loop. An
   invalid output path (bad directory, no permission) would only surface
   once every record had already run through the composer and any LLM
   calls - wasted latency and cost before an unrecoverable failure. Fixed
   by opening both output streams before the loop (fail-fast restored,
   verified by pointing `--output` at a nonexistent directory and
   confirming it now fails immediately) while still deferring the actual
   array *write* until after accumulation, so the JSON-array format is
   unaffected.
2. **Stale doc comment.** `AgentJsonOptions.Default`'s comment still said
   "JSONL file" after this same PR changed output to a JSON array. Updated.
3. **Synchronous I/O in an async pipeline.** `IRecordWriter<T>.WriteAll`
   was a blocking synchronous call inside `CliRunner.RunAsync`. Changed to
   `Task WriteAllAsync(TextWriter, IEnumerable<T>, CancellationToken)`,
   using `TextWriter.WriteAsync(ReadOnlyMemory<char>, CancellationToken)`
   so cancellation actually propagates through the write, not just the
   agent calls.

**Deferred, with reasoning recorded rather than silently dropped:**

4. **LOH / intermediate string allocation in `JsonArrayRecordWriter`.**
   True that `JsonSerializer.Serialize(records, options)` builds one
   contiguous string before writing it. The suggested fix (serialize
   directly to a stream/`Utf8JsonWriter`) would require `IRecordWriter<T>`
   to stop being `TextWriter`-based - which is also what backs
   `StringWriter` in every unit test for this writer. Reworking a public
   interface to avoid an allocation that, for this project's actual data
   (2-12 records), is a few KB, is disproportionate now. Recorded as a
   real, known limitation rather than an oversight.
5. **Crash/cancellation loses accumulated-but-unwritten output.** This is
   the same tradeoff already called out explicitly in this file's "Post-MVP
   hardening: output format changed from JSONL to a JSON array" entry
   above - the reviewer's comment confirms it as a legitimate concern, not
   news, and the reasoning for accepting it stands: a genuine unhandled
   crash is already outside the intended per-record-isolation contract.

**No action (informational):**

6. Praise/confirmation that the `problem_statement.txt` vs. `BACKLOG.md`
   distinction was reasoned correctly - no change requested.
7. A security note that `UnsafeRelaxedJsonEscaping` leaves `<`, `>`, `&`
   unescaped, with the caveat "fine for files and API payloads, not for
   raw HTML embedding" - correct, and this JSON is never embedded in HTML,
   so no change needed.

Also resolved a real merge conflict (PR #9's branch had fallen behind `dev`
after PR #7 and #8 merged) - only `TalkingPoints.md` conflicted, since the
code changes touched disjoint files; the two "Post-MVP hardening" sections
were combined in chronological order.

Final: 122 tests, 100% line/branch/method coverage across `Agent.Tests`

## Sprint 8: Logging

### Scope

This sprint is documentation only. No source file was changed while producing
it. It was requested after Sprint 7's live crash-and-fix raised three
questions in quick succession: where is the logging and what are we logging
to, do we have enough catch blocks and are error messages capturing file/line
detail, and why doesn't the 100% code-coverage gate catch a missing catch
block. This section answers all three with a complete inventory rather than
spot examples, so the gaps are visible as a whole instead of one at a time.

### There is no logging framework anywhere in this codebase

A full grep of `src/` for `ILogger`, `Serilog`, `LoggerFactory`, `Console.`,
and `WriteLine` returns exactly five hits, and all five are in one file:

| File:Line | What it writes |
|---|---|
| `src/Agent.Cli/CliRunner.cs:37` | Usage message when `--input`/`--output` are missing |
| `src/Agent.Cli/CliRunner.cs:54` | Composer-selection failure message |
| `src/Agent.Cli/CliRunner.cs:100` | Per-record batch failure message |
| `src/Agent.Cli/CliRunner.cs:133` | Per-record eval-scoring failure message |
| `src/Agent.Cli/Program.cs:9` | Wires `Console.Out`/`Console.Error` into `CliRunner`'s constructor |

That is the entire observability surface of the application. There is no
`ILogger<T>`, no structured logging, no log levels (info/warn/error/debug),
no log file, no correlation ID, and no timestamp on any emitted line. Every
message above is a bare `TextWriter.WriteLine` call writing plain text to
stderr (or stdout for the eval report). `Agent` (the core library) contains
none of these five lines - it never writes anything anywhere. All
observability, such as it is, lives entirely in the CLI shell, one layer
above every business rule.

The closest thing to structured output in the whole system is the
`--diagnostics` JSON artifact (`TaskDiagnostics`: `consent_verified`,
`fair_housing_check_passed`, `brand_style_applied`, `safety_violation_count`)
and the `--eval-report` text file. Both are domain artifacts describing what
the agent decided for a given input, not logs describing what the process
did while deciding it. Neither carries a timestamp, a log level, or any
indication of *when* or in what order records were processed. If asked "what
happened when record 7 ran," the honest answer today is "read the exception
message printed to stderr, if any was printed at all."

### Complete catch-block inventory

A full grep for `catch (` in `src/` returns exactly eight blocks:

| # | File:Line | Catches | What happens |
|---|---|---|---|
| 1 | `Agent.Cli/CliRunner.cs:52` | `ArgumentException`, `InvalidOperationException` (composer selection) | `error.WriteLine(ex.Message)` - **message only**, no exception type, no stack trace |
| 2 | `Agent.Cli/CliRunner.cs:94` | `Exception` (per-record batch loop) | `error.WriteLine($"Record '{taskId}' failed: {ex.GetType().Name}: {ex.Message}")`, then `continue` - type + message, no stack trace |
| 3 | `Agent/Common/TimeZones.cs:11` | `TimeZoneNotFoundException`, `InvalidTimeZoneException` | Re-thrown as `ArgumentException` with the original passed as `innerException` - the detail is preserved on the new exception object, but nothing downstream ever reads `InnerException` when displaying an error, so it is captured and then never shown |
| 4 | `Agent/Evaluation/Evaluator.cs:40` | `Exception` (per-record scoring, added in Sprint 7) | `RecordScore.Unscoreable(taskId, $"{ex.GetType().Name}: {ex.Message}")` - type + message, no stack trace |
| 5 | `Agent/Composition/OpenAiMessageComposer.cs:74` | `HttpRequestException`, `InvalidOperationException`, `JsonException` (the completion call) | `Result<NextMessage>.Failure($"Completion request failed: {ex.Message}")` - message only |
| 6 | `Agent/Composition/OpenAiMessageComposer.cs:84` | `JsonException` (deserializing the model's response) | `Result<NextMessage>.Failure($"Model response was not valid JSON: {ex.Message}")` - message only |
| 7 | `Agent/Domain/LenientExpectedOutcomeConverter.cs:23` | `JsonException` (parsing the `expected` oracle field) | `return null` - **fully silent**, no message captured anywhere, see below |
| 8 | `Agent/Ingest/JsonlRecordReader.cs:30` | `JsonException` (parsing one input line) | Re-thrown as `InvalidDataException($"Line {lineNumber} failed to parse.", ex)` - line number added, original exception preserved as `InnerException`, but again nothing ever prints `InnerException` |

Observations that fall out of this table directly:

- **Only 2 of 8 catch blocks capture the exception type** (`ex.GetType().Name`)
  alongside the message (#2 and #4, both from Sprint 7's fix). The other six
  catch specific exception types already, so the type is implicit in the
  `catch` clause itself rather than in the emitted text - but that means the
  *emitted text* still reads identically whichever specific exception in that
  clause fired, e.g. block #1 cannot tell you from its output alone whether
  it was the `ArgumentException` or the `InvalidOperationException` branch.
- **Zero of 8 catch blocks capture or print a stack trace.** Every message on
  screen is exactly as informative as `ex.Message` and nothing more. This
  matters because the unhandled crash that started Sprint 7 (before the fix)
  produced .NET's default fatal-exception output, which includes the full
  stack trace with file and line number - meaning the *default, un-designed*
  crash output was strictly more useful for debugging than any of the
  deliberate error handling in this codebase.
- **Two blocks (#3, #8) preserve `InnerException` but nothing ever reads it
  back.** Wrapping an exception with more context is only half the pattern;
  the other half - a top-level handler or logger that walks `InnerException`
  and prints the full chain - does not exist. Today, catching these wrapped
  exceptions anywhere prints only the outer message (e.g. `"'X' is not a
  recognized time zone id."`), and the original `TimeZoneNotFoundException`
  detail is inert.
- **Block #7 is the one fully silent path in the system.** No message, no
  log line, no diagnostic flag - if a record's `expected` field fails to
  parse, the only observable symptom is that record scoring as if the field
  were absent (e.g. showing up as "no expected outcome" in an eval report).
  There is no way to distinguish "this record genuinely has no oracle" from
  "this record's oracle was malformed and we swallowed the parse error" by
  looking at any output the system produces.

### The inconsistency at CliRunner.cs:54

`CliRunner.cs` has three `error.WriteLine` calls inside catch blocks
(lines 54, 100, 133). Lines 100 and 133 were touched during Sprint 7's fix
and both include `ex.GetType().Name` (or, for 133, the already-typed
`ScoringError` string built the same way inside `Evaluator`). Line 54 - the
composer-selection failure, e.g. an unrecognized `--composer` value or a
missing `OpenAI:ApiKey` user secret - was left exactly as it was before
Sprint 7 and still reads `error.WriteLine(ex.Message)`, with no exception
type. This is a real, current inconsistency in the codebase: two of three
sibling error sites in the same file follow one convention, the third
follows another, and nothing enforces consistency between them because there
is no shared error-formatting helper - each site builds its own string
inline. Per the instruction for this sprint, it was left unchanged so it
could be documented here rather than fixed silently.

### Per-record isolation is a CLI-layer convention, not a library guarantee

The try/catch at `CliRunner.cs:94` around `agent.RunAsync(...)` and the one
inside `Evaluator.Evaluate` (added in Sprint 7) are the only two places in
the entire system that isolate one record's failure from the rest of the
batch. Neither `LeasingMessageAgent` (the orchestrator) nor
`ValidatingMessageComposer` (which sits between the raw composer and the
safety validator) contains a single try/catch of its own - confirmed by
reading both files in full during this audit. Both rely entirely on the
`Result<T>` type for *expected* failures (a composer declining to produce a
safe message, a validator rejecting output) and simply let any *unexpected*
exception propagate unguarded through every layer until it reaches whichever
caller happens to have wrapped the call - today, that is `CliRunner` for the
main batch and `Evaluator` for scoring. If either of those call sites were
ever removed, or if a third caller (a future web API, a queue worker) were
built directly against `LeasingMessageAgent` without its own try/catch, one
malformed record would once again take down an entire run, exactly as it did
before the Sprint 7 fix - because the isolation lives at the edges the CLI
happened to add, not inside the library's own contract.

### Why 100% code coverage did not catch any of this

The coverage gate (`test.ps1`, threshold 100% line/branch/method) measures
whether *lines that exist* were executed by at least one test. It has no way
to express or enforce "a catch block should exist here." The Sprint 7 crash
is the clearest possible proof of this: at the moment the real interview
data crashed the process with an unhandled `ArgumentNullException`, the test
suite was reporting 100% coverage on every metric it tracks. Coverage was
never wrong - every line that existed had been exercised - but the missing
catch block around eval scoring was, by definition, zero lines of code, and
zero lines cannot fail a line/branch/method threshold. The same is true of
the fully-silent catch at `LenientExpectedOutcomeConverter.cs:23`: the
`catch (JsonException) { return null; }` line itself is covered by a test
(a case with a deliberately malformed `expected` field), so coverage reports
it as fully green, while the *absence* of any logging or signal inside that
block is invisible to any coverage tool by construction. Coverage answers
"did we run this code," never "is this code doing enough."

### What real production logging would need

This is not a recommendation to implement now - the interview this data is
from is already complete, and Sprint 8's scope is analysis only - but for
completeness, closing this gap for real would mean:

- **`ILogger<T>` injected into `LeasingMessageAgent`, `ValidatingMessageComposer`,
  `OpenAiMessageComposer`, and `CliRunner`**, replacing the raw
  `TextWriter`/`Console` dependency with the standard .NET logging
  abstraction, so call sites log rather than print.
- **Log levels** - today every emitted line is equivalent severity (plain
  text to stderr); a real system needs `LogWarning` for a single degraded
  record versus `LogError` for something that aborts a run versus `LogDebug`
  for per-record timing that already exists in-memory (`Stopwatch` is
  captured in `ScoredRun` today but never logged, only used for scoring).
- **A correlation ID per record** - `ProspectCase.TaskId` already exists and
  is unique per record; it should be attached to every log line for that
  record's processing (e.g. via `ILogger` scopes), not just appended to the
  final error string as it is today.
- **A real sink** - a log file, or structured JSON logs to stdout for
  container log collection, rather than unstructured text interleaved with
  the eval report on the same stdout/stderr streams.
- **Exception detail that includes the type and full `ToString()` (stack
  trace included) at minimum for unexpected exceptions**, reserving the
  short `ex.Message`-only style for expected, already-typed failure paths
  where the message alone is genuinely sufficient (e.g. `Result<T>.Failure`
  cases, which are business outcomes, not bugs).
- **A single shared error-formatting helper** so the `CliRunner.cs:54`-style
  drift (three call sites, two conventions) cannot recur - today each catch
  block builds its own string inline with no shared code to keep them
  aligned.
and `Agent.Cli.Tests`.

## Post-review fix round: PR #11 (code review + Antigravity/Gemini)

### Scope

Both an automated code review and the Antigravity (Gemini 3.8 Flash) reviewer
left findings on PR #11 (Sprint 7's live hold-out fix). Triaged all of them on
their merits rather than applying every suggestion - two of the eight Gemini
comments recommended changes that this project had already deliberately
rejected in earlier sprints, documented above.

### Fixed

1. **`CaseConstraints.PrimaryCta` is now genuinely nullable (`string?`).** This
   was the actual root cause both reviewers converged on: `PrimaryCtaVocabulary.ToCtaType`
   was patched to accept and return `string?` when Sprint 7's crash was fixed,
   but the domain record itself - the actual source of the null - stayed
   declared non-nullable, so every *other* caller (present or future) was
   still working under a false compiler guarantee. Fixing the type at its
   source, rather than patching callers one at a time, resolved two separate
   findings for free:
   - `PrimaryCtaVocabulary.ToCtaType` gained `[return: NotNullIfNotNull(nameof(primaryCta))]`,
     which tells the compiler the converse also holds (a non-null input can
     never come back null) - this cleared a real `CS8604` warning in
     `TemplateMessageComposer.cs` that a `dotnet build -t:Rebuild` reproduced
     during review, without relying on the incidental upfront
     `IsNullOrWhiteSpace` guard that happened to make it safe at runtime.
   - An explicit JSON `"primary_cta": null` (as opposed to a merely missing
     key) no longer crashes `JsonlRecordReader.ReadAll` with an unhandled
     `InvalidDataException` at ingestion - `RespectNullableAnnotations` only
     rejects an explicit null against a non-nullable property, so an honestly
     nullable property accepts it the same way it already accepted the
     missing-key case. New test: `ReadAll_ExplicitNullPrimaryCta_ParsesToNullInsteadOfThrowing`.
2. **The CTA instruction moved outside `<prospect_data>`.** The prompt told
   the model to treat everything inside that block as untrusted, non-instructional
   data, then put the one piece of text actually meant to steer the model's
   CTA choice - especially the no-required-type fallback, which has no
   schema-level enforcement backstop - inside it anyway. `BuildUserPrompt` now
   builds a `ctaInstruction` sentence placed in the instructional preamble,
   before the `<prospect_data>` block opens, for both the required and
   fallback cases. This also resolves Gemini's separate wording complaint
   (`required_cta_type: none specified - choose a reasonable call to action`
   read as an oxymoron to a model) since the new fallback phrasing
   ("No specific call to action is required; choose one reasonable for this
   message.") is a plain sentence, not a key/value pair. New tests:
   `ComposeAsync_UserPrompt_RequiredCtaInstructionPlacedOutsideProspectDataBlock`,
   `ComposeAsync_UserPrompt_NoPrimaryCtaConstraint_StatesNoSpecificCtaRequiredOutsideProspectDataBlock`.
3. **Extracted the duplicated exception-formatting expression.** `CliRunner.cs`
   and `Evaluator.cs` each independently built `$"{ex.GetType().Name}: {ex.Message}"`
   inline - flagged by the code review, and independently called for by
   Sprint 8's own logging audit ("a single shared error-formatting helper so
   the `CliRunner.cs:54`-style drift... cannot recur"). Added
   `Agent.Common.ExceptionFormatting.ToDiagnosticString()` and pointed both
   call sites at it.

### Declined (with reasons)

1. **Gemini's suggestion to give `TemplateMessageComposer` a fallback CTA
   when `PrimaryCta` is absent, instead of refusing the record.** This is not
   a new observation - the earlier "lenient parsing of the `expected` oracle"
   hardening pass already found and discussed this exact behavior, and
   concluded `TemplateMessageComposer` "correctly refuses to compose from" a
   null `PrimaryCta`, identifying the real gap as elsewhere (the orchestrator
   folding a legitimate refusal into the same "suppressed" shape as a
   no-consent case) - a different, already-tracked issue, not this one.
   Reversing that already-reasoned decision on this PR would contradict the
   project's own prior conclusion without new evidence.
2. **Gemini's suggestion to filter `Evaluator`'s catch with
   `when (ex is not OperationCanceledException)`.** The principle is correct
   in the abstract, but `Evaluate`/`Score` take no `CancellationToken` and
   call nothing cancellable - there is no path by which `Score(run)` can
   throw `OperationCanceledException` today. Adding the filter would add an
   untestable branch (no honest test can force that exception without
   inventing an unrelated seam), which both contradicts this project's
   stated practice of not writing tests to poke at unreachable code and would
   fail the 100% branch-coverage gate outright.

### After

159 tests in `Agent.Tests` (up from 156), 12 in `Agent.Cli.Tests`, 100%
line/branch/method coverage on both, zero build warnings.

## Sprint 9: Logging implementation

Sprint 8 was analysis only - a full inventory of every catch block and the
complete absence of a logging framework, with no source file touched. This
sprint acts on that inventory: real `Microsoft.Extensions.Logging` is wired
through the library and the CLI, closing every gap Sprint 8 named as
"what would be needed for real production logging."

### Scope decisions, made with the user before writing any code

Two choices were confirmed up front rather than assumed, since both affect
the shape of the change across nearly every file in `src/`:

1. **Logging stack: `Microsoft.Extensions.Logging` + console JSON + a small
   custom file provider**, not Serilog. This stays inside the
   `Microsoft.Extensions.*` family already used for `IConfiguration`, adding
   no new dependency family, and still closes the "a log file" gap Sprint 8
   flagged via a purpose-built `FileLoggerProvider` rather than a
   general-purpose logging package.
2. **Fix the one fully-silent catch too** (`LenientExpectedOutcomeConverter`).
   `JsonConverter<T>` instances are stateless and shared across every
   deserialization call (`AgentJsonOptions.Default` is a static singleton),
   so there is no constructor-injection path into it - closing this gap
   required one narrow, explicitly-documented exception to the
   constructor-injection pattern used everywhere else (`AgentLog`, below).

### What was built

**`Agent.Common.AgentLog`** - an `AsyncLocal<ILoggerFactory?>`-backed static
accessor, used only by `LenientExpectedOutcomeConverter`. Not a plain static
field: a plain static would let concurrently-running callers - parallel unit
test runs included - stomp on each other's configured factory, since
`AsyncLocal` only flows a value down the async call tree from where it was
set, not across unrelated concurrent calls. `CliRunner.RunAsync` calls
`AgentLog.Configure(loggerFactory)` once, covering the entire run including
the `JsonlRecordReader.ReadAll` parse (where the converter runs).

**`ILogger<T>` injected into every class Sprint 8's catch-block inventory
named**, each via a trailing optional constructor parameter defaulting to
`NullLogger<T>.Instance` (`ILogger<T>? logger = null`) rather than a required
parameter - this is what kept the change additive: every existing test and
every existing call site (`RealAgentFactory`, `Program.cs`, every other test
file) kept compiling and passing completely unchanged, because C# allows
omitting a trailing optional parameter. Only the handful of tests written to
prove the new logging behavior needed to pass a real logger.

- `LeasingMessageAgent.RunAsync` opens a log scope carrying `TaskId` at the
  top of the method, covering every downstream call (composer, validator)
  for the lifetime of that one record - so a correlation ID exists for the
  first time in this codebase's logs, without threading `TaskId` through
  every method signature down the call chain. Logs `Information` on a
  successful compose, `Information` when suppressed for no consent,
  `Warning` when composition fails, `Warning` when final safety validation
  still finds a violation.
- `ValidatingMessageComposer.ComposeAsync` logs `Warning` on each rejected
  attempt (both a safety-violation rejection and a `Result.Failure` from the
  inner composer, with the failure reason attached), `Warning` before
  falling back to the safe composer, and `Error` if even the fallback fails
  composition or safety validation - the retry/fallback behavior Sprint 8
  noted had zero observability of its own now has all three outcomes visible.
- `OpenAiMessageComposer.ComposeAsync` logs `Warning` with the full exception
  object (not just `ex.Message`) in both of its existing catch blocks - the
  completion-request failure and the malformed-JSON-response failure.
- `Evaluator.Evaluate` opens a log scope carrying `TaskId` per record (same
  pattern as the agent, applied to the separate eval-scoring pass) and logs
  `Error` with the full exception in its per-record catch - the exact catch
  block Sprint 7 added and Sprint 8 flagged as capturing only
  `ex.GetType().Name` and `ex.Message` in the returned string, never a stack
  trace anywhere.
- `LenientExpectedOutcomeConverter`'s catch now logs `Warning` (via
  `AgentLog`, not constructor injection - see above) before returning
  `null`, closing the one path Sprint 8 identified as producing zero signal
  of any kind on failure.

**`Agent.Cli.Logging.FileLoggerProvider`** - a from-scratch `ILoggerProvider`
implementing `ISupportExternalScope`, so a scope opened anywhere downstream
(the `TaskId` scopes above) renders in the log file exactly the way the
console provider renders it: `LoggerFactory` hands every
`ISupportExternalScope` provider the one shared scope stack it manages.
Special-cases a scope built from a key/value collection (what
`BeginScope(new Dictionary<string, object>{...})` produces) to render its
pairs directly, rather than the collection's bare `ToString()` (a
`Dictionary`'s default `ToString()` is just its type name - the first
version of this written for this sprint hit exactly that bug, caught by a
test asserting the actual `TaskId` value appeared in the file content, not
just that the file was non-empty).

**`CliRunner`** wires it all together: a new `--log-file <path>` option,
`BuildLoggerFactory` (console provider with the JSON formatter, scopes
enabled, plus the file provider when `--log-file` is given), and every
constructed component (`LeasingMessageAgent`, `ValidatingMessageComposer`,
`OpenAiMessageComposer`, `Evaluator`) now receives its own `ILogger<T>` from
that factory. The three existing `error.WriteLine` catch sites gained a
matching `log.LogError`/`log.LogWarning` call **alongside**, not instead of,
the existing text - the plain CLI error text is a stable contract every
existing `CliRunnerTests` assertion depends on, and breaking that to
"purify" the design into logging-only would have been scope creep against
working tests, not a fix. The two channels serve different purposes: the
plain text is the CLI's user-facing contract; the structured log is a
separate, leveled, scope-carrying observability channel.

**The `CliRunner.cs:54` inconsistency Sprint 8 flagged** (bare `ex.Message`
where two sibling catch sites used `ex.ToDiagnosticString()`) was fixed as
part of this sprint, not left for later - it is the literal code this
sprint's `log.LogError(ex, "Composer selection failed.")` call sits next to,
so fixing it here closes the exact gap it was flagged for rather than
introducing a fourth inconsistent site.

### A real bug found and fixed while building this

`LoggerFactory.Create(...)`'s internal DI container does not reliably
dispose an `ILoggerProvider` **instance** handed to it via `AddProvider` -
it did not construct that instance itself, so it does not assume ownership
of disposing it (a documented, if easy-to-miss, .NET DI behavior: the
container only disposes what it created). The first version of
`BuildLoggerFactory` constructed `FileLoggerProvider` inline inside the
`LoggerFactory.Create` builder callback and trusted `loggerFactory`'s own
`using` to dispose it - which left the log file's `StreamWriter` handle
open, and every test that both wrote to `--log-file` and then tried to
delete that file in its own cleanup failed with
`IOException: ... being used by another process`. This was not a transient
Windows file-locking flake (a bounded delete-retry helper was tried first
and still failed every attempt) - it reproduced on every run. Fixed by
having `CliRunner` construct and own the `FileLoggerProvider` itself in an
explicit `using` declaration, independent of `loggerFactory`'s own
lifetime; `StreamWriter.Dispose()` is idempotent, so the belt-and-suspenders
double-dispose (ours, plus whatever `LoggerFactory` does or doesn't do) is
harmless.

### After

198 tests total (174 in `Agent.Tests`, up from 159; 24 in
`Agent.Cli.Tests`, up from 12), 100% line/branch/method coverage on both
projects, zero build warnings. New packages:
`Microsoft.Extensions.Logging.Abstractions` (`Agent`),
`Microsoft.Extensions.Logging` and `Microsoft.Extensions.Logging.Console`
(`Agent.Cli`) - all from the same `Microsoft.Extensions.*` family as the
`Microsoft.Extensions.Configuration` packages already in use, version-pinned
to 9.0.0 to match.

Every claim in Sprint 8's "what real production logging would need" list is
now true of this codebase: `ILogger<T>` is injected everywhere a catch block
was inventoried, log levels distinguish a degraded-but-handled outcome
(`Warning`) from a genuinely unexpected one (`Error`), `TaskId` is a real
correlation ID carried via log scope rather than string-concatenated per
call site, a real log file exists behind `--log-file`, and the
`CliRunner.cs:54` inconsistency is gone. What remains out of scope, by
choice: no example in this codebase yet demonstrates the correlation ID
tying together log lines emitted by two *different* CLI invocations (e.g. a
retry from a queue) - `TaskId` is unique within one run's input file, not
globally, which is the right scope for a CLI batch tool and would be the
first thing to revisit if this ever became a hosted service instead.
