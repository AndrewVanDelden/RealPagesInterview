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

_Pending._

### After

_Pending._

---

## Sprint 5: Orchestrator and CLI

### Before

_Pending._

### After

_Pending._

---

## Sprint 6: Eval harness

### Before

_Pending._

### After

_Pending._

---

## Sprint 7: Live hold-out runbook and narrative

### Before

_Pending._

### After

_Pending._
