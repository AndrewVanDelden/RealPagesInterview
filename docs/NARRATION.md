# How the next-best-message agent works, out loud

Filled from Appendix B of `~/.agent-rules/PROJECT_PLAYBOOK.md`. The seven questions are the
five-minute answer; questions 1 and 2 alone are the sixty-second answer. While speaking: no
pattern names, no class names, one file name at most (question 3), and walk one record rather
than walking the parts. Question 6 is updated after every run that changes the number.

## The seven questions

**1. What it is.** A file of prospect and resident records goes in, one JSON object per line,
and a JSON array comes out, one row per record, saying which channel to use, when to send, what
the message says, and what the next action is. A record nobody is allowed to contact still gets
a row: suppression is a decision the output states, not an absence.

**2. The steps, in order.** Six, and they run in this order every time.

1. Pick the contactable channel: walk the record's own preference order and take the first one
   its consent opts into. Nothing consented means suppress and stop.
2. Plan the next action: count the days from the run's reference date to the move date, call
   that horizon short or long, and look the action up by the record's persona and lifecycle
   stage, falling back to one generic row when no row covers that pair.
3. Write the message, in the record's language.
4. Schedule the send: the channel's hour on the first day at or after the later of the run's
   reference time and the record's last interaction, in the record's own timezone.
5. Validate the finished message: the opt-out line is present, no Social Security number, no
   long run of digits, no fair-housing steering. A violation suppresses the message.
6. Emit the row, plus a per-record account of how each of the five decisions above was reached.

**3. Where the decisions live.** One file: `src/Agent/Orchestration/LeasingMessageAgent.cs`. It
reads top to bottom as those six steps, one numbered comment each, and it holds no rule of its
own: every step hands the question to the component that owns it and takes the answer back.

**4. What code decides and what it delegates.** Steps 1, 2, 4 and 5 are deterministic code with
no I/O at all, and the run's clock is a value the caller passes in, so the same file at the same
reference time gives the same answer on every machine on any day. Step 3 is the only step that
can reach outside the process, and only when the run asks for it: by default the prose comes
from a template set compiled into the program, and a flag sends it to a model instead. The model
never picks the channel, the time or the action; it writes prose and chooses a call-to-action
type from a list the code hands it. The evaluation report can also ask a model to grade two of
its checks, and that is off unless asked for too.

**5. How it fails.** Bad arguments exit 1 and nothing runs. A line that will not parse becomes
one failure row naming its line number, every other record still runs, and the process exits 2.
Inside a record there are three suppressions and they are different facts: no consented channel,
which is the correct answer rather than a failure; prose that could not be written at all; and a
finished message that failed safety, which also writes a row to a review queue file carrying the
draft that was rejected, so a person can read what was refused. When the model path times out,
the template writes the message and the per-record account names the fallback, so a degraded run
looks different from a clean one instead of looking the same.

**6. How I know it works.** An evaluator scores every output field against the label the
customer wrote, per record and per check, and a proof test corrupts a correct output to show the
scorer can fail rather than only pass.

Last result, 2026-09-09, template composer, reference times as documented:

- `sample.jsonl`, the two records any rule is fitted to: every check 2 of 2, 2 of 2 records pass,
  exit code 0.
- `holdout_12.jsonl`: channel 12 of 12, send day 7 of 11, send hour 5 of 11, action type 7 of 12,
  opt-out 11 of 11, call-to-action type 7 of 11, payload 11 of 11, language 11 of 11, safety
  12 of 12, personalization 8 of 8; 4 of 12 records pass every check; exit code 0.
- `synthetic_12.jsonl`: every check perfect, 12 of 12 records pass, exit code 2 for its one
  deliberately malformed line.
- Zero safety violations and an empty review queue on all three.

The numbers that would change what to fix are send day and send hour, and they are one fact
rather than two: the oracle uses a per-stage send day and hour that the two fitted records never
showed, so the scheduler answers with the only rule the evidence supports.

**7. What I would change.** The message body still has no check with teeth on a normal run. The
personalization proxy reads 1.00 on every message because the template inserts both facts it
counts, so that number cannot fall; the check that could fall is the model judge, and it is off
by default. I would make the body's grade part of the number the run reports rather than an
opt-in flag.

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
Every value below is from the run of 2026-09-09 that wrote `out_sample.json` and
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
