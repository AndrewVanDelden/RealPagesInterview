# Talking points

One sentence per point. Grouped so the first section is the 60-second answer and each
later section is one level deeper. Full evidence lives in `docs/RETROSPECTIVE_2026-09-06.md`.

## What it is

- It reads one prospect record and produces the next message to send and the next action to take.
- Every decision is code; the model only writes prose, behind a validator and a template fallback.
- A scorer compares every record's output to its labeled answer, so "done" is a number, not a claim.

## How one record moves through it

- Check whether any preferred channel has consent; if none, suppress and plan a follow-up only.
- Pick the first preferred channel that has consent.
- Write the message with the template or the model, retry once on a safety violation, then fall back to the template.
- Pick the send time from the channel's default hour in the prospect's timezone.
- Plan the next action from the move-in horizon.
- Run the safety check on the final text and suppress on any violation.

## Decisions made before code

- C# on .NET 10 because the job description lists it and the problem statement names no stack.
- Strict TDD with a 100 percent line, branch, and method coverage gate that breaks the build.
- The OpenAI key lives in `dotnet user-secrets`, is set by Andrew, and is never handled by the agent.
- All work on `dev`, one PR per sprint, `main` untouched until the epic merges.
- The `required_states` are proof obligations written to a separate diagnostics file, never into the graded output.
- The model boundary is prose only: channel, timing, and action are deterministic and unit-testable.

## Decisions per component

- Consent and channel selection share one `IsOptedIn` source so they cannot disagree.
- Channel returns an option type, one convention for absence across the codebase.
- Send time is the channel default hour with a next-day rollover; a quiet-hours window was scoped out as absent from the problem.
- Next action is the move-date horizon against a 45-day threshold, a constant fitted to two samples.
- Composers return a result type so the model path can fail without throwing and the template path can never fail.
- The OpenAI client is plain `HttpClient` with Structured Outputs and a strict schema, no SDK dependency.
- Prospect data sits in a delimited block in the prompt with every instruction outside it.
- The validator is three keyword and regex checks with word-boundary matching, disclosed as a proxy, never a compliance system.
- The compose-validate loop is bounded to two attempts by construction and validates the fallback too.
- The orchestrator holds no rules and sequences the components in one method.
- The evaluator scores captured results, never re-running the agent, so the report describes what was written.
- Output is one indented JSON array because the problem statement constrains only the input format.
- Logging is `Microsoft.Extensions.Logging` with a task id scope on every per-record line and a file sink behind `--log-file`.

## What the reviews caught

- `Option.Value` on none returned the zero enum member, a real channel, instead of throwing.
- A missing `expected.next_message` was non-nullable in one type and nullable in its twin.
- Steering terms matched substrings, so "Colorado" tripped "color" and a prospect named Christian tripped a religion.
- The bare word "stop" satisfied opt-out, so "bus stop" passed.
- A formatted card number evaded the digit-run pattern until the pattern tolerated separators.
- The fallback composer's output shipped unvalidated on the one path that most needed checking.
- The eval harness ran the agent a second time, so the report could describe a different sample than the output file.
- Moving the output write after the loop lost fail-fast on a bad path until the streams were opened first.
- Console logging was hardwired to the real console and flooded the test runner until it wrote through the injected stream.
- A file logger handed to the logger factory was never disposed, which locked the log file in every test.

## What the hold-out showed

- The interviewer's 12 unseen records scored 2 of 12 with the evaluator, 1 of 12 if send time were scored.
- Ten of twelve records had no move date and no last interaction, so both defaulted silently to year 0001.
- The planner reads only the move date, so every one of those ten planned a new-prospect welcome cadence.
- `persona`, `lifecycle_stage`, and `language` were parsed and read by no code.
- The expected action is a lookup on lifecycle stage and consent; the horizon rule fit the two samples by coincidence.
- The evaluator never scored send time, body, subject, or CTA payload, and compared CTA type to the agent's own input.
- Personalization scored 1.00 on every record because it counted the two fields the template always inserts.
- `brand_style_applied` and `consent_verified` were literal `true`.
- Two records with no `primary_cta` were suppressed entirely when the oracle expected an email.
- A no-consent resident got `start_cadence` because the plan was computed before the consent check and never revised.
- Eight input fields the hold-out carried were dropped silently because unknown members are skipped by default.
- Seven year-0001 plans produced no log line, because the logs record exceptions and not decisions.

## Why it happened

- The two samples were treated as the specification, and every rule cited them as evidence.
- Both samples were new prospects with move dates, so a horizon rule and a stage rule fit both points equally.
- The evaluator was built last, in 30 minutes, to check the fields the agent already produced.
- A synthetic rehearsal set found the year-0001 dates before the interview and the finding was parked as out of scope.
- The rules in force governed code shape, tests, and coverage; nothing governed truth against data.
- Coverage counts executed lines, and a rule that was never written has no line to count.
- SOLID and DRY on everything produced 66 files, 40 under 15 lines, and 8 interfaces with one implementation, which cost explainability, not correctness.
- The whole system is 40 lines in one method, and nothing pointed at it, which is why "how does it work" drew a blank.

## What changed in the process

- Earned Abstraction replaced the SOLID and DRY mandate: an interface needs a second implementation on the day it is created.
- Stable Current Sync replaced cutting-edge language sync, because the newest constructs hallucinate most.
- Pillar 3 added Phase First and Decisions Before Tasks: state the phase and next step, and open every plan with decisions.
- Verify First now covers inferred rules: apply each to every example and name any other field that fits the same values.
- Every AGENTS.md carries a Current phase section that an agent reads before proposing work.
- The playbook was audited step by step and 27 steps rewritten to remove one project's artifacts posing as universal rules.
- A two-question probe run in a scratch folder with no docs shows whether a rule change altered behavior.
- A narration guide gives seven questions and a rehearsal rule: narrate one record through the code aloud before any PR merges.
- Fresh chats now state the phase, refuse later-phase artifacts, and name confounds without any document telling them to.

## Decisions to finish the project

- D9 (the frame, 2026-09-07): the twelve-record file is an evaluation set, never fitted to; the two samples are the only evidence a rule is fitted to; the honest number is reported, never targeted.
- D1 Input contract (landed Sprint 2): three required members, every other member nullable and named per record when absent, unknown members kept and listed by path, an unknown channel name is a real `Unknown` value.
- D2 Decision model: consent first (landed), then a catalog with the rows the samples justify plus one generic row, "no policy" as a result, never the nearest rule.
- D3 Output contract (shape landed Sprint 2): suppression is a `next_message` object with channel `none`, a suppression reason in diagnostics, required states earned by checks that can fail.
- D4 Scheduling: the channel's slot on or after the later of reference time and last interaction, in the record's timezone, scored to day and hour; minutes not modeled.
- D5 Composition: templates keyed on persona and channel from the facts the record carries, English shipped, other languages to the model composer, options on SMS, link on email, missing CTA means the generic reply.
- D6 Evaluation: every output field scored, scorer proven able to fail, both the twelve and a frozen synthetic set reported as evaluation sets.
- D7 Structure: keep the three interfaces with substitutes, remove the eight without, six named steps in the orchestrator.
- D8 Gates (landed Sprint 1): CI on every push, branch protection on `dev`.
- D10 (landed Sprint 2): `--now` is the run's reference time, a value passed in; nothing in the library reads a clock.
- D11 and D12: the twelve live in the repo beside the samples; Phase 0 restarted at step 1.

## Definitions worth having ready

- A hold-out is test data kept from you while you build; passing it proves generalization, passing the samples proves nothing.
- Overfitting is code and tests fit to the examples you had, so they agree with each other and disagree with unseen data.
- Coverage measures whether tests ran the lines that exist; mutation testing measures whether the tests would notice a change.

## Curveballs, answered in four sentences

- A resident with no move date: today the date defaults to year 0001 and it confidently plans a welcome cadence; step five owns it; key the planner on stage and consent; nine of twelve hold-out actions go from wrong to labeled.
- A record with no consented channel: today the message is suppressed and the action is still a cadence; step one owns it; the consent verdict flows into the plan as `no_op`; the opt-out record becomes a scored row.
- A field the samples never showed: today it is dropped silently; ingestion owns it; log every unrecognized field per record; the log names the gap on the first run.
