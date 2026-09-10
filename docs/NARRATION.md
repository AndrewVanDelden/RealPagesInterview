# How the next-best-message agent works, out loud

Filled from Appendix B of `~/.agent-rules/PROJECT_PLAYBOOK.md`. The script below answers all
seven questions in 150 words, one minute spoken, one paragraph per question and questions 2 and 3
together. Rules while speaking: no pattern names, no class names, one file name at most, and walk
one record rather than the parts. The number in question 6 is updated after every run that
changes it.

## One minute

It reads leasing records and writes one row each: channel, send time, message and next action. A
record nobody may contact still gets a row saying so.

Six steps run in one file, `LeasingMessageAgent.cs`. It picks the first consented channel, looks
up the next action by persona and stage, writes the message in their language, schedules it in
their timezone, runs four safety checks, and emits the row with its reasons.

Code makes every decision. A model, when asked, only writes the prose, and the safety gate checks
it first.

A bad line becomes one failure row and the batch keeps going.

An evaluator scores each field against the customer's labels. The hold-out passes 4 of 12; it
misses on send times and vocabulary the samples never showed.

Only the model judge can fail a weak message body, and it's off by default. I'd make it part of
every run.

Last result, 2026-09-10, tag `v1.0.0`, template composer, reference times as documented:
`holdout_12.jsonl` 4 of 12, `synthetic_12.jsonl` 12 of 13 with its malformed line an `ERROR` row,
zero safety violations on both.

## Curveballs I have rehearsed

- **A timezone the runtime does not know.** Today it schedules in UTC and the per-record account
  says the zone it used was UTC, rather than inferring a zone from the send time. Step 4 owns it.
  Fix: none until a labeled record states a different expectation, because the alternative is
  guessing a zone and hiding the guess. Proof: `synthetic_08_unknown_timezone`, which passes send
  day and send hour with `time_zone_id` reading `UTC`.
- **A required state the program has no check for.** Three hold-out records assert
  `renewal_offer_loaded` and nothing in the program can earn it. Today it is recorded by name as
  `no_check_defined`, neither claimed nor failed. Step 6 owns it. Fix: write the check when a
  labeled record shows what would earn it. Proof: those three records, whose state map names it.
- **A record that asks for quiet hours.** Today it gets the channel's fixed hour anyway, 09:00
  local for SMS and 10:00 for email, because no quiet-hours window is modeled and no input file
  has ever carried one. Step 4 owns it. Fix: read the window off the record and move the slot to
  the first hour inside it. Proof: a record carrying the window whose `send_at` moves and whose
  send hour still scores.
- **Steering stated as meaning rather than as a listed term.** A body reading "perfect for young
  professionals" passes the fair-housing check today, because that check matches terms and spans,
  not intent, and the message ships. Step 5 owns it. Fix: put the body through the semantic judge
  as a gate rather than only as a report line. Proof: that body going from a clean row to a review
  queue row.
- **A persona and lifecycle stage no row covers.** Today the generic row answers confidently and
  the per-record account says so by naming its source, so the wrongness is visible rather than
  silent; five of the hold-out's action misses are exactly this. Step 2 owns it. Fix: add the row
  when a labeled record shows the pair. Proof: `synthetic_02_unseen_persona_and_stage`, which the
  generic row answers correctly, against the hold-out records where it does not.

## The rehearsal: one record, file by file

`prospect_welcome_day0`, the first line of `sample.jsonl`, at `--now 2025-12-09T00:00:00-06:00`.
Every value below is from the run of 2026-09-10 at tag `v1.0.0` that wrote `out_sample.json` and
`diag_sample.json`.

- **`src/Agent.Cli/Program.cs` into `src/Agent.Cli/CliRunner.cs`.** The flags are parsed, every
  component is built once, and the per-record loop opens the one log scope carrying this record's
  task id. Nothing downstream passes the id explicitly, and nothing downstream opens a second
  scope.
- **`src/Agent/Ingest/JsonlRecordReader.cs`.** Line 1 becomes one result holding one record. It
  states `task_id`, `consent` and `channel_preferences`, the three required members; it does not
  state `input.profile.amenity_interest`, so the ingest notes list that path as defaulted, and it
  carries no member the program does not know, so the unknown list is empty.
- **Step 1, `src/Agent/Decisions/ChannelSelector.cs`.** The preference order is `sms` then
  `email`; consent opts into both, so the first one wins and the channel is SMS. Reaching this
  step is what earns `consent_verified`: the consent-driven selection is the step that owns the
  state, and there is no second component asking the same question.
- **Step 2, `src/Agent/Common/TimeZones.cs` then `src/Agent/Decisions/NextActionPlanner.cs` and
  `src/Agent/Decisions/ActionCatalog.cs`.** The reference time in `America/Chicago` is the local
  date 2025-12-09. The move date is 2026-01-10, so the horizon is 32 days, under the 45-day
  threshold, so the branch is short. The catalog has a row for persona `prospect` and stage `new`
  and that row states the short branch, so the action is `start_cadence` named
  `prospect_welcome_short_horizon` and the source is recorded as `catalog_row` rather than the
  generic fallback.
- **Step 3, `src/Agent/Safety/ValidatingMessageComposer.cs` wrapping
  `src/Agent/Composition/TemplateMessageComposer.cs`.** The record's language is `en`, so
  `MessageTemplateCatalog` hands over the English set and records that the locale was applied.
  `CallToActionCatalog` turns the record's `primary_cta` of `book_tour` into type `schedule_tour`
  with the SMS reply options `Thu` and `Fri`, and the link stays null because this is SMS. The
  body comes back as "Hi Taylor! Welcome to Oak Ridge Apartments. We heard you're looking in
  Richardson, TX. Reply to book a tour. Reply 1 for Thu, 2 for Fri. Reply STOP to opt out." The
  wrapper validates that draft, it is clean, and the attempt count is 1.
- **Step 4, `src/Agent/Decisions/SendScheduler.cs`.** The last interaction, 2025-12-08T15:04Z, is
  before the reference time, so the floor is the reference time and that is recorded. The SMS
  hour is 09:00, the first 09:00 at or after the floor in `America/Chicago` is 2025-12-09, and
  the zone reaches that wall time exactly once, so the slot is recorded as exact and `send_at` is
  2025-12-09T09:00:00-06:00.
- **Step 5, `src/Agent/Safety/SafetyValidator.cs` and
  `src/Agent/Safety/BrandStyleValidator.cs`.** This is the orchestrator's own gate on the
  finished message rather than borrowed trust in the composer: all four checks pass, the
  violation count is 0, and the fair-housing state is that one check's own verdict rather than
  "nothing went wrong anywhere". All three brand rules hold, so the failed-rule list is empty.
- **Step 6, `src/Agent/Ingest/JsonArrayRecordWriter.cs` and
  `src/Agent/Evaluation/Evaluator.cs`.** The row is written with channel `sms`, the send time
  above, the body above and the action above, and beside it the account of every decision: states
  all three earned, zero violations, no suppression, the plan, the schedule and the composition.
  The evaluator scores that row against the record's own `expected` and every check reads OK.

Two things to listen for while saying this out loud, both of which are findings if they happen:
needing to search to find the next step, and saying "then it goes through an interface to the
only class that implements it." Three interfaces exist in the library, and each has a real
implementation and an offline one; no step above hops through an interface to a single
implementation.
