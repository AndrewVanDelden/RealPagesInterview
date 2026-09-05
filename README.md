<div align="center">

# Next-Best-Message Agent

**RealPage take-home: a context-aware, autonomous message-sending agent.**

![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Coverage](https://img.shields.io/badge/coverage-100%25-brightgreen)
![Methodology](https://img.shields.io/badge/methodology-TDD-blue)
![License](https://img.shields.io/badge/license-take--home-lightgrey)

</div>

Given a JSONL record of a prospect's profile, consent, and context, the
agent decides **whether** to communicate, **how** (channel), **when** (send
time), and **what** to say — then emits structured output that semantically
matches the graded expectation. Every decision except the message's prose is
deterministic, unit-tested code; the LLM is confined to a single
"compose the prose" node behind an interface, with a validator standing
between it and the outside world.

## Architecture

```mermaid
flowchart TD
    A[Ingest JSONL record] --> B{Consent gate}
    B -- no consented channel --> S[Suppress: next_message = null]
    B -- contactable --> C[Select channel: first preferred with opt-in]
    C --> D[Compose message: LLM node or template]
    D --> E{Validate: opt-out, no PII, no steering}
    E -- violations --> D
    E -- clean, or retry exhausted --> F[Schedule send_at: timezone-aware]
    F --> G[Plan next_action: horizon-based]
    S --> G
    G --> H[Emit AgentOutput JSON]
```

Every box above is one small, single-responsibility class behind its own
interface — swappable and independently unit-tested. Full sequence diagram,
interface table, and the SOLID mapping: [docs/DESIGN.md](docs/DESIGN.md).

## Layout

| Path | What it is |
|---|---|
| `src/Agent` | The library: domain records, decision logic, composition, safety. No I/O beyond the completion client. |
| `src/Agent.Cli` | Console entry point — thin shell over `Agent`. |
| `tests/Agent.Tests` | xUnit tests. One suite, 100% line/branch/method coverage, enforced as a build-breaking gate (not just reported). |
| `docs/` | Design, backlog, and code-review-process documentation (see below). |

## Getting started

```bash
dotnet build      # builds the whole solution (.slnx)
.\test.ps1        # runs the suite with the coverage gate; fails the build under 100%
```

The end-to-end CLI (`dotnet run --project src/Agent.Cli -- --input sample.jsonl --output out.json`)
lands in the orchestrator sprint — see [TalkingPoints.md](TalkingPoints.md) for exactly what's
built as of any given point in the log. Input stays JSONL (that part is in
`problem_statement.txt`); output is a single indented JSON array, not JSONL — readability
mattered more than a self-imposed symmetry once we noticed the problem statement never
required it.

Add `--eval-report <file>` against a labeled file (one with `expected` populated,
like `sample.jsonl`) to get a scorecard proving the agent meets its thresholds -
channel, `next_action.type`, opt-out/CTA presence, safety, personalization, and
latency, per record and overall - printed to the console and written to that file.

Add `--log-file <file>` for a real, structured log of what the process did
while producing that output - full flag reference, log format, and how to
debug a bad run: [docs/OPERATIONS.md](docs/OPERATIONS.md).

## Documentation

- [docs/DESIGN.md](docs/DESIGN.md) — architecture, interface table, inferred decision rules and their evidence, assumptions log, security/governance posture.
- [docs/BACKLOG.md](docs/BACKLOG.md) — the epic and sprint plan with acceptance criteria.
- [docs/OPERATIONS.md](docs/OPERATIONS.md) — how to run it, how to debug a bad run, how the logging actually works, and how to read one log line.
- [TalkingPoints.md](TalkingPoints.md) — the running Before/After decision log, one entry per sprint. The source of truth for "what's actually done right now."
- [docs/CODE_REVIEW.md](docs/CODE_REVIEW.md) — what the two automated PR reviewers check for, and the scope decisions they should not re-flag.

## Engineering notes

Built test-first throughout: a failing test before any implementation, every
sprint landing as its own PR with a 100%-coverage gate. A few of the more
interesting calls, in brief (full rationale in TalkingPoints.md):

- **`.slnx`** instead of a legacy `.sln` — plain XML, no GUID soup.
- **Central package management** (`Directory.Packages.props`) — one place for every dependency's version.
- **`System.Text.Json` with `JsonNamingPolicy.SnakeCaseLower`** for the snake_case JSONL schema — no per-property attribute mapping needed for the vast majority of fields.
- **`[GeneratedRegex]`** (source-generated, not `new Regex(...)`) for the safety validator's PII patterns.
- **`Option<T>` / `Result<T>`** in `Agent.Common` instead of nulls or thrown exceptions for expected "no value" / "expected failure" cases.
- Domain types never reuse .NET BCL names (caught and fixed a real `Channel` vs. `System.Threading.Channels.Channel` collision during Sprint 1).
