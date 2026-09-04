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

---

## Sprint 2: Deterministic decisions

### Before

_Pending._

### After

_Pending._

---

## Sprint 3: Message composition

### Before

_Pending._

### After

_Pending._

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
