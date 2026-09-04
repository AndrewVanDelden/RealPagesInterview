# Epic: Next-Best-Message Agent

Sprints ordered for a focused build. The goal is a runnable end-to-end path by
the end of Sprint 5, with the eval harness proving thresholds in Sprint 6. Each
task lists its acceptance criteria. Everything follows test-first design:
tests express the spec, then the implementation makes them pass.

Suggested timebox for a roughly four-hour window is in brackets per sprint.

---

## Sprint 0: Scaffolding [15 min]

**Goal:** a solution that builds with empty projects wired together.

- **0.1 Create solution and projects.**
  - `src/Agent` (library), `src/Agent.Cli` (console), `tests/Agent.Tests` (xUnit).
  - Acceptance: `dotnet build` succeeds; test project references the library.
- **0.2 Repo hygiene.**
  - `.gitignore` for .NET, `bin/`, `obj/`, and secrets. `README` pointer to `docs/`.
  - Acceptance: no secret or build artifact is tracked.
- **0.3 Git remote and secrets setup.** *(user-run, not agent-run)*
  - Initialize git, connect to
    [github.com/AndrewVanDelden/RealPagesInterview](https://github.com/AndrewVanDelden/RealPagesInterview),
    push the initial scaffold. Initialize `dotnet user-secrets` in `Agent.Cli`
    and set the OpenAI API key locally, outside the repo, never handled or
    written by the agent.
  - Acceptance: the scaffold is pushed and visible on GitHub for full code
    review; `dotnet user-secrets list` from `Agent.Cli` shows `OpenAI:ApiKey` set.

## Sprint 1: Domain models and ingest [25 min]

**Goal:** parse a record into typed models.

- **1.1 Domain records.**
  - Records for the input schema and the output (`AgentOutput`, `NextMessage`,
    `Cta`, `NextAction`). Result and option types in `Common/`.
  - Acceptance: models are immutable records with explicit types; no BCL name reuse.
- **1.2 JSONL reader.**
  - Read a file line by line into records; skip blank lines; snake_case mapping.
  - Acceptance: a test parses `sample.jsonl` into 2 records with fields populated.

## Sprint 2: Deterministic decisions [55 min]

**Goal:** every non-prose decision, fully unit tested.

- **2.1 Consent gate.**
  - Contactable if any preferred channel is opted in; record `consent_verified`.
  - Acceptance: opted-in returns contactable; all-off returns suppress.
- **2.2 Channel selector.**
  - First channel in preferences with matching consent.
  - Acceptance: sample 1 returns sms; sample 2 returns email; none-consented returns none.
- **2.3 Send scheduler.**
  - Timezone-aware, quiet-hours window, channel default hour (sms 09:00, email 10:00).
    Quiet-hours window scoped out; see [CODE_REVIEW.md](CODE_REVIEW.md#known-deliberate-scope-decisions).
  - Acceptance: sms case resolves 09:00 local; email case resolves 10:00 local; a
    late-night `last_interaction` pushes into the allowed window.
- **2.4 Next-action planner.**
  - Short horizon starts a cadence; long horizon schedules a follow-up.
  - Acceptance: move within 45 days returns `start_cadence`; beyond returns `follow_up_in_days`.

## Sprint 3: Message composition [50 min]

**Goal:** produce the message text, LLM-backed with an offline fallback.

- **3.1 Composer interface and template implementation.**
  - `IMessageComposer` plus `TemplateMessageComposer` that builds a compliant,
    personalized message deterministically.
  - Acceptance: body contains first name, property, interest, primary CTA, and opt-out text.
- **3.2 Completion client interface.**
  - `ICompletionClient` with an OpenAI implementation and a fake for tests.
  - Acceptance: a test drives `OpenAiMessageComposer` with a fake client that
    returns canned structured JSON, no network call.
- **3.3 OpenAI composer.**
  - Prompt enforces brand voice, CTA, opt-out, fair-housing guardrails; requests
    structured JSON; small fast model for latency.
  - Acceptance: returns a typed message; malformed model output is rejected, not crashed.

## Sprint 4: Safety and fair-housing validator [30 min]

**Goal:** nothing unsafe leaves the agent.

- **4.1 Validator.**
  - Enforce opt-out presence, no PII leak, no steering or protected-class language;
    count violations; record `fair_housing_check_passed`.
  - Acceptance: missing opt-out yields a violation; a steering phrase yields a
    violation; a clean message yields zero.
- **4.2 Compose-validate loop.**
  - On violations, request one corrected composition, then hard-stop with a safe
    fallback so the loop is bounded.
  - Acceptance: a seeded bad draft is corrected or replaced; the loop never runs unbounded.

## Sprint 5: Orchestrator and CLI [30 min]

**Goal:** end-to-end on the sample file.

- **5.1 Orchestrator.**
  - Run the pipeline, assemble `AgentOutput`, carry diagnostics (states, violations).
  - Acceptance: sample 1 produces sms + `start_cadence`; sample 2 produces email + `follow_up_in_days`.
- **5.2 CLI.**
  - `--input`, `--output`, `--composer template|openai`. Read JSONL, write JSONL.
  - Acceptance: `dotnet run -- --input sample.jsonl --output out.jsonl` writes 2 valid output lines.

## Sprint 6: Eval harness [30 min]

**Goal:** prove the thresholds rather than assert them.

- **6.1 Scorer.**
  - Compare channel, `next_action.type`, constraints, personalization score, latency.
  - Acceptance: running against `sample.jsonl` reports both records passing on
    channel and action, with a personalization score at or above the minimum.
- **6.2 Scorecard output.**
  - Per-record table plus overall pass/fail.
  - Acceptance: a readable table prints to console and to a file.

## Sprint 7: Live hold-out runbook and narrative [15 min]

**Goal:** be ready to run 12 hold-outs live and talk through the design.

- **7.1 Runbook.**
  - One page: set the key, run the CLI on the hold-out file, export the 12 outputs.
  - Acceptance: the steps work from a clean shell.
- **7.2 Interview narrative.**
  - Map each design decision to the assumptions log and the governance section.
  - Acceptance: a short talking-track that ties the architecture to the thresholds
    and the fair-housing posture.

---

## Optional stretch (only if time remains)

- **Python port** of the same pipeline for a second reference implementation.
- **Reply classifier** stub to speak to `reply_classification_f1`.
- **Semantic personalization score** using embeddings rather than token overlap.
