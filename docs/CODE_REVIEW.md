# Code Review Process

Every PR into `dev` goes through two automated multi-agent reviewers in
addition to Andrew's own pass. This document records what each one checks,
so a finding can be traced back to a known checklist item, and so a
deliberate scope decision (see below) isn't repeatedly flagged as an
oversight by either reviewer or by a human reading their comments later.

## Claude Code review: 8 review angles

A multi-agent PR review splits the task across eight specific perspectives:

| # | Angle | Focus |
|---|---|---|
| 1 | Line-by-Line Scan | Sequential, granular check of added code for immediate syntax problems, typos, or clear logical bugs. |
| 2 | Removed-Behavior Audit | Targeted investigation into what was deleted, to ensure no critical background dependency or legacy configuration was broken. |
| 3 | Cross-File Tracer | How the change impacts external files, ensuring updated parameters or schema changes stay aligned across the codebase. |
| 4 | Reuse Check | Scans for duplication to catch a reinvented helper or utility that should have been reused instead. |
| 5 | Simplification Check | Readability and refactoring: overly complex logic, deep nesting, redundant expressions. |
| 6 | Efficiency Check | Computational and resource health: performance bottlenecks, memory leaks, unoptimized queries. |
| 7 | Altitude Check | Steps back to macro-architecture: does the code match the high-level design, system boundaries, and business logic. |
| 8 | Conventions Check | Strict adherence to project styling, formatting, and file structure (this repo's own working agreement and CLAUDE.md-equivalent rules). |

## Gemini / Antigravity review: criteria

The second reviewer applies the review criteria in `AGENTS.md` (section
"Review criteria") plus the universal code pillars loaded at user scope,
cited by acronym: VF, LC, EA, SD, HR, SCU, EET, HSC, SCS, BC, HB, PF, DBT;
the key and the evidence for each are in `~/.agent-rules/CODE_PILLARS.md`.
The pillar text is not reproduced here; `AGENTS.md` is the single project
source and the pillars' source lives outside the repo. A finding must name a
correctness defect, a stated requirement, or a pillar. An empty review
outputs exactly "Nothing to report."

The earlier version of this section listed a "SOLID and DRY" mandate and a
"Cutting-Edge Language Sync" rule. Both were retired on 2026-09-05 and
replaced by Earned Abstraction (EA) and Stable Current Sync (SCS).

## Known, deliberate scope decisions

Findings below are not gaps. They're recorded here so a reviewer (automated
or human) doesn't re-flag them as missing behavior.

- **No diagnostics object for the channel decision (Sprint 5, D23).** `AgentDiagnostics`
  explains consent, the action (`action_plan`, D18) and the send (`schedule`, D22), and
  deliberately says nothing about how the channel was chosen. A decision earns an object
  when its working cannot be read off the input and the output; the channel's working is
  `channel_preferences` in the record's stated order intersected with `consent`, both in
  the input, and `next_message.channel` is the answer. Confirmed by the requester on
  2026-09-08. Do not flag it as a gap in the Phase 3 check, which is passed.

- **Quiet-hours window (original sprint plan, Sprint 2.3).** `SendScheduler` does not
  model a separate configurable quiet-hours window. `problem_statement.txt`
  and `sample.jsonl`'s `assertions`/`thresholds` never mention quiet hours;
  the concept only appeared in this project's own `DESIGN.md` elaboration of
  the two samples. Andrew confirmed it is not part of the actual required
  task and to scope it out. The single day-rollover rule `SendScheduler`
  does implement (push to tomorrow if today's default-hour slot has already
  passed relative to `last_interaction`) independently satisfies all three
  of Sprint 2.3's stated acceptance criteria. See `docs/DESIGN.md` section 8
  (non-goals) and assumptions A4 and A5, and the comment on `SendScheduler`.

- **Fair-housing/PII heuristic, not semantic understanding (original sprint
  plan, Sprint 4.1).** `SafetyValidator` enforces opt-out presence, PII patterns,
  and protected-class/steering language via keyword and regex matching, not
  a semantic or LLM-based check. A paraphrase of steering language (e.g.
  "we prefer residents without young children" instead of "families only")
  would not be caught. This is a scoped, disclosed limitation for a 30-minute
  sprint, not an oversight - see the comment on `SafetyValidator`.
  `CaseConstraints.NoSensitiveDiscrimination`
  is deliberately never read: the protected-class/steering check always
  runs regardless of its value, since fair housing law has no legitimate
  per-case opt-out (unlike opt-out messaging or generic PII sensitivity,
  which can vary case by case).

- **Timezone resolved more than once per record (PR #17 review).** `IngestNotes`,
  `LeasingMessageAgent`, and `SendScheduler` each independently resolve the same
  record's `TimeZoneInfo`. Flagged as an efficiency finding; not fixed. `TimeZoneInfo`
  already caches by id internally (a lock-protected dictionary lookup), so the marginal
  cost is a few extra cached lookups per record, not repeated OS/registry work. Threading
  one resolved `TimeZoneInfo` through `IngestNotes.Describe`, `LeasingMessageAgent`, and
  `ISendScheduler.Resolve` would mean changing `ISendScheduler`'s signature and every test
  built against it (`SendSchedulerTests` passes a raw timezone id string throughout) for a
  gain that duplicates work the BCL is already doing. Not worth the churn.

- **Language detection is a stop-word heuristic for two languages (Sprint 3, D13 c).**
  `LanguageDetector` counts disjoint function words for English and Spanish, the languages
  the labeled sets contain, and reports any other stated language as not measured. It is not
  a general language identifier and does not need a dependency to become one until a third
  language appears in a labeled set.

- **Personalization cannot fail the template composer (Sprint 3, D13 a).** The scorer counts
  the first name and the property name because those are the only facts every label carries
  (sample 1 omits its city, hold-out 11 omits its amenities), and the template inserts both
  by construction. The check can fail (`ScorerProofTests` corrupts it); it cannot fail this
  composer. The body judge of Sprint 6 (D5, D15) is the check with teeth for content.

- **Replay aligns by position (Sprint 3, D14).** The graded output carries no task id
  (playbook step 27), so `--replay` pairs rows with the parsed records in order and refuses a
  count mismatch. A task id in the output, or a diagnostics file as a second replay input,
  would be extra surface for a mode that scores files this program wrote.

- **The model judge is not in the harness yet (Sprint 3, D15).** Section 6's semantic score
  for `next_action.type` and the body judge land together in Sprint 6, off by default, so one
  pinned model and one rubric have one owner. Until then the action type is exact only.

- **CliRunner's ingest-notes log line is not level-gated (PR #17 review).** Flagged
  because `log.LogInformation(...)` builds two joined strings as ordinary method
  arguments before the call, so they're computed even when Information logging is
  filtered out. Not fixed: `CliRunner.BuildLoggerFactory` hardcodes
  `SetMinimumLevel(LogLevel.Information)` with no CLI flag to raise it, so
  `log.IsEnabled(LogLevel.Information)` is always `true` in this codebase today - adding
  the guard would be an always-true branch the coverage gate can't exercise on the
  `false` side. Revisit if a `--log-level` flag is ever added.
