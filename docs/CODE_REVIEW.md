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

## Gemini / Antigravity review: 2 pillars

The second reviewer grades against the same two pillars used to evaluate
this whole take-home (see `TalkingPoints.md` kickoff decisions):

**Pillar 1 - Foundational Architecture (SOLID & DRY)**

- Single Responsibility (SRP): one reason to change per class/module.
- Open/Closed (OCP): open for extension, closed for modification.
- Liskov Substitution (LSP): subtypes substitutable for base types.
- Interface Segregation (ISP): small, role-specific interfaces.
- Dependency Inversion (DIP): depend on abstractions, not concretions.
- DRY: eliminate duplication by extracting shared logic.

**Pillar 2 - AI-Native Optimization & Syntax Recency**

- Small Context Units (SCU): short, hyper-focused files.
- Extreme Explicit Typing (EET): strict static typing everywhere.
- High Semantic Clarity (HSC): highly descriptive, intention-revealing names.
- Cutting-Edge Language Sync: the latest native language features over
  legacy patterns or external libraries.

## Known, deliberate scope decisions

Findings below are not gaps. They're recorded here so a reviewer (automated
or human) doesn't re-flag them as missing behavior.

- **Quiet-hours window (BACKLOG.md Sprint 2.3).** `SendScheduler` does not
  model a separate configurable quiet-hours window. `problem_statement.txt`
  and `sample.jsonl`'s `assertions`/`thresholds` never mention quiet hours;
  the concept only appeared in this project's own `DESIGN.md` elaboration of
  the two samples. Andrew confirmed it is not part of the actual required
  task and to scope it out. The single day-rollover rule `SendScheduler`
  does implement (push to tomorrow if today's default-hour slot has already
  passed relative to `last_interaction`) independently satisfies all three
  of Sprint 2.3's stated acceptance criteria. See `docs/DESIGN.md`
  assumptions log #2 and the comment on `SendScheduler`.
