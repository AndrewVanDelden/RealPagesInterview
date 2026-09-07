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
