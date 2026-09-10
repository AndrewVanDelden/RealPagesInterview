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
  plan, Sprint 4.1; narrowed in Sprint 7, D40 and D41).** `SafetyValidator` enforces
  opt-out presence, PII patterns, and protected-class/steering language via keyword
  and regex matching, not a semantic or LLM-based check. Sprint 7 fixed the cases a
  probe run of 37 inputs through the patterns as written proved wrong (D41), so what
  follows is what is still scoped out, not the whole heuristic. Four misses stay,
  each with the reason from D41 rather than an opinion:
  semantic paraphrase (e.g. "we prefer residents without young children" instead of
  "families only"), which needs understanding rather than a pattern; letter spacing
  (`f a m i l i e s`) and interior punctuation (`fam*ilies`), because matching across
  arbitrary separators would make `color` fire on unrelated letter sequences, trading
  these misses for a larger false-positive class; and Cyrillic homoglyphs, because
  the text under validation is written by this system's own composer, not by an
  adversary who controls the bytes (A18). Do not re-flag any of the four; see also
  the comment on `SafetyValidator`.
  `CaseConstraints.NoSensitiveDiscrimination`
  is deliberately never read: the protected-class/steering check always
  runs regardless of its value, since fair housing law has no legitimate
  per-case opt-out (unlike opt-out messaging or generic PII sensitivity,
  which can vary case by case).
  `no_pii_leak` now gates one identifier check and not the other, and that split is
  deliberate (D40), not an inconsistency to flag. `SocialSecurityNumber` is
  unconditional: no leasing message legitimately carries one, so there is no case the
  flag would be protecting, and a record setting `no_pii_leak: false` is saying it does
  not need the heuristic, not that it consents to a leak. `LongDigitRun` stays gated on
  the flag, because it is a proxy that also matches a confirmation number or a tour
  reference (D41), and a record with a legitimate long identifier needs a way to say so.
  `OptOutInstructions` stays gated on `include_opt_out_instructions`, because
  transactional exemptions are real and the record is the only thing that knows whether
  this message is one.
  `Validate_PiiCheckNotRequired_LeakedIdentifierIsNotAViolation` asserted the old
  behavior and is inverted rather than deleted, so the contract change stays visible in
  the diff.

- **Timezone resolved more than once per record (PR #17 review).** `IngestNotes`,
  `LeasingMessageAgent`, and `SendScheduler` each independently resolve the same
  record's `TimeZoneInfo`. Flagged as an efficiency finding; not fixed. `TimeZoneInfo`
  already caches by id internally (a lock-protected dictionary lookup), so the marginal
  cost is a few extra cached lookups per record, not repeated OS/registry work. Threading
  one resolved `TimeZoneInfo` through `IngestNotes.Describe`, `LeasingMessageAgent`, and
  `SendScheduler.Resolve` would mean changing `SendScheduler`'s signature and every test
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

- **Brand style cannot fail the template composer (Sprint 7, D42).** All three brand rules
  (the opt-out instruction on the body's last non-blank line, at most one exclamation mark,
  a subject present exactly when the channel is email) are satisfied by the template composer
  by construction, so `brand_style_applied` reads true on every record of every documented
  run and no tally moves. That is disclosed rather than left to be discovered, and it does
  not make the check the hardcoded `true` it replaces: it is computed from the message, a
  test proves each rule can fail, and the composer it has teeth against is the model path,
  whose subject, punctuation and closing line are the model's to get wrong. D39 classifies
  it as a diagnostic that never suppresses, because an off-voice message is off-voice and
  not unlawful. Same shape as the personalization entry above.

- **`renewal_offer_loaded` is deliberately never earned (Sprint 7, D42).** The states map
  gives every name in a record's own `required_states` a verdict, and this one is recorded
  as not earned, by name. Hold-out records name it, and some of them carry a
  `renewal_offer_id` a rule could obviously be fitted to, which is exactly why no rule is
  written for it: the twelve-record file is an evaluation set and nothing is fitted to it
  (D9, A19). Not earned is the honest answer and it is the answer A14 already committed to.
  Do not flag it as a missing check.

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

- **No by-hand re-scoring of the examples (Sprint 9, D63).** Playbook step 81 asks that the
  examples be run end to end from the documented command and the output checked by hand
  against the expected records. The per-field, per-record comparison against the label is
  exactly what `Evaluator` already does, under D13 d, which fixes the label as the oracle and
  forbids scoring against the product's own tables; `--eval-report` prints one verdict per
  check per record and a per-check tally, and `ScorerProofTests` is what proves the scorer can
  fail. A person redoing that across 26 rows would run the same comparison less reliably, and
  a disagreement would mean the scorer is wrong rather than the output. What is kept from step
  81 is the half no automated check makes: that each documented command produces the output
  file, the diagnostics file, the review queue and the scorecard and exits with the documented
  code, that the scorecard's tally lines agree with its own rows (`Scorecard` computes its
  tallies once in field initializers, so a `with` copy that replaces `RecordScores` prints a
  report whose rows and totals disagree), and that one record read end to end across its four
  artifacts tells one consistent story. Recorded as a dated subsection of `docs/DESIGN.md`
  section 9. Do not flag the absence of a manual scoring pass as a missing step.

- **Disk full is proved as the open that fails, not as a full volume (Sprint 9, D64).**
  Playbook step 84 names disk full on output as a fault to inject. Sprint 9 gives `--output`,
  `--diagnostics`, `--review-queue` and `--eval-report` the same fail-fast guard on
  `IOException` and `UnauthorizedAccessException` that `--log-file` already has, so every
  cause of a failed open, a missing directory, a read-only path, a locked file, a volume
  that is already full, produces one clean stderr line naming its own flag and exit code 1
  rather than an unhandled exception. What the guard does not cover, stated rather than
  implied: on `--output`, `--diagnostics` and `--review-queue` it sits at the open, so a
  volume that fills after the file is opened throws from inside the writer, downstream of
  every guard in `CliRunner`, and that run still ends on an unhandled exception with an exit
  code that is none of the three documented ones. (`--eval-report` is the one written in a
  single guarded call rather than through a stream held open across the batch, so its own
  write is covered.) Producing an actual out-of-space condition, at the open or after it, is
  scoped out: it needs a virtual disk or a filesystem quota, which is machine setup this
  suite cannot carry and CI cannot reproduce, and at the open it would exercise the same
  catch the guard's own tests already exercise. The six faults and their proofs, including
  that uncovered case, are in `docs/FAULT_INJECTION.md`. Do not flag the absence of a real
  out-of-space test.
