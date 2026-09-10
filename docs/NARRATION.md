# How the next-best-message agent works, out loud

Filled from Appendix B of `~/.agent-rules/PROJECT_PLAYBOOK.md`. The script below answers all
seven questions in 240 words, a minute and a half spoken, one paragraph per question and
questions 2 and 3 together. Rules while speaking: no pattern names, no class names, one file name
at most, and walk one record rather than the parts. The number in question 6 is updated after
every run that changes it.

## One minute

It reads leasing prospect records and writes one row for each: which channel, when to send, the
message, and what to do next. A record nobody may contact still gets a row saying so.

Six steps run in order in one file, `LeasingMessageAgent.cs`. Pick the first channel the prospect
prefers and has consented to. Look up the next action by who they are, where they are in the lease
cycle, and how far off their move date is. Write the message in their language. Schedule it at the
channel's hour in their timezone. Run four safety checks. Emit the row with a reason for every
decision.

Code makes every decision. A language model, only when asked for, writes the prose, and the same
safety gate checks its draft before anything ships.

It fails three ways. A line that will not parse becomes one error row and the batch keeps going.
If the model is down or its draft fails safety, a fixed template writes the message instead and
the row says so. If no rule covers a prospect, a generic fallback answers and the row names that
too.

An evaluator scores each field against the customer's own labels. The hold-out passes 4 of 12; the
misses are send times and vocabulary the two samples never showed.

Only the model judge can fail a weak message body, and it is off by default. I would make it part
of every run.

Last result, 2026-09-10, the code at tag `v1.0.0`, template composer, reference times as
documented: `holdout_12.jsonl` 4 of 12, `synthetic_12.jsonl` 12 of 13 with its malformed line an
error row, zero safety violations on both.

## Curveballs I have rehearsed

- **A timezone the runtime does not know.** Today it schedules in UTC and the per-record account
  says UTC was the zone it used, rather than guessing a zone from the send time. Step 4 owns it.
  Fix: none until a labeled record states a different expectation, because the alternative is
  guessing a zone and hiding the guess. Proof: `synthetic_08_unknown_timezone`, which passes send
  day and send hour with the zone recorded as UTC.
- **A required state the program has no check for.** Three hold-out records assert
  `renewal_offer_loaded` and nothing in the program can earn it. Today it is recorded by name as
  having no check, neither claimed nor failed. Step 6 owns it. Fix: write the check when a labeled
  record shows what would earn it. Proof: those three records, whose state map names it.
- **A record that asks for quiet hours.** Today it gets the channel's fixed hour anyway, 09:00
  local for SMS and 10:00 for email, because no quiet-hours window is modeled and no input file
  has ever carried one. Step 4 owns it. Fix: read the window off the record and move the slot to
  the first hour inside it. Proof: a record carrying the window whose send time moves and whose
  send hour still scores.
- **Steering stated as meaning rather than as a listed term.** A body reading "perfect for young
  professionals" passes the fair-housing check today, because that check matches terms and spans,
  not intent, and the message ships. Step 5 owns it. Fix: put the body through the semantic judge
  as a gate rather than only as a report line. Proof: that body going from a clean row to a review
  queue row.
- **A persona and lifecycle stage no row covers.** Today the generic row answers confidently and
  the per-record account says so by naming its source, so the wrongness is visible rather than
  silent. Four of the hold-out's five action misses are exactly this; the fifth is a row that
  exists but states no action for that horizon, which the account names as a different fact. Step
  2 owns it. Fix: add the row when a labeled record shows the pair. Proof:
  `synthetic_02_unseen_persona_and_stage`, which the generic row answers correctly, against the
  hold-out records where it does not.

## The rehearsal: one record, file by file

`prospect_welcome_day0`, the first line of `sample.jsonl`, at `--now 2025-12-09T00:00:00-06:00`.
Every value below is from a run on 2026-09-10 of the code at tag `v1.0.0`, with the diagnostics
file and the evaluation report switched on.

- **`src/Agent.Cli/Program.cs` into `src/Agent.Cli/CliRunner.cs`.** The flags are parsed and
  every component is built once. Records run up to four at a time, and each one runs inside its
  own log scope carrying its task id; nothing downstream passes the id or opens a second scope.
  Before this record runs, the same file notes what it left out: no amenity interest, so that one
  path is listed as defaulted, and no member the program does not know, so the unknown list is
  empty.
- **`src/Agent/Ingest/JsonlRecordReader.cs`.** Line 1 becomes one parsed record. It carries the
  three required members, the task id, consent and channel preferences, and that is all this file
  decides.
- **Step 1, `src/Agent/Decisions/ChannelSelector.cs`.** The preference order is SMS then email;
  consent covers both, so the first one wins and the channel is SMS. Reaching this step is what
  earns the consent state, and nothing later asks the question again.
- **Step 2, `src/Agent/Common/TimeZones.cs`, then `src/Agent/Decisions/NextActionPlanner.cs` and
  `src/Agent/Decisions/ActionCatalog.cs`.** The reference time in Chicago is the local date
  2025-12-09. The move date is 2026-01-10, so the horizon is 32 days. At most 45 days is short, so
  the branch is short. The catalog has a row for a new prospect, and that row states the short
  branch, so the action is start a cadence named `prospect_welcome_short_horizon`, and the account
  records that a catalog row answered rather than the generic fallback.
- **Step 3, `src/Agent/Safety/ValidatingMessageComposer.cs` wrapping
  `src/Agent/Composition/TemplateMessageComposer.cs`.** The record's language is English, so the
  English prose set is used and the account records that the locale was applied. The record's
  call to action, book a tour, maps to the wire type `schedule_tour` in the call-to-action table,
  which holds only the type and an email link path. The reply options Thu and Fri come from the
  English set in `src/Agent/Composition/EnglishMessageTemplates.cs`, keyed by that type, and the
  link stays null because this is SMS. The body comes back as "Hi Taylor! Welcome to Oak Ridge
  Apartments. We heard you're looking in Richardson, TX. Reply to book a tour. Reply 1 for Thu, 2
  for Fri. Reply STOP to opt out." The wrapper validates that draft, it is clean, and the attempt
  count is 1.
- **Step 4, `src/Agent/Decisions/SendScheduler.cs`.** The last interaction, 2025-12-08T15:04Z, is
  before the reference time, so the floor is the reference time and that is recorded. The SMS
  hour is 09:00, the first 09:00 at or after the floor in Chicago is 2025-12-09, and the zone
  reaches that wall time exactly once, so the slot is recorded as exact and the send time is
  2025-12-09T09:00:00-06:00.
- **Step 5, `src/Agent/Safety/SafetyValidator.cs` and
  `src/Agent/Safety/BrandStyleValidator.cs`.** This is the orchestrator's own gate on the
  finished message, not borrowed trust in the composer. All four checks pass, the violation count
  is 0, and the fair-housing state is that one check's own verdict rather than "nothing went wrong
  anywhere". All three brand rules hold, so the failed-rule list is empty.
- **Step 6, back in `src/Agent/Orchestration/LeasingMessageAgent.cs`.** The result goes back to
  the loop: the row, with channel SMS, the send time above, the body above and the action above,
  and beside it the account of every decision, all three states earned, zero violations, no
  suppression, the plan, the schedule and the composition.
- **After the batch, `src/Agent.Cli/CliRunner.cs` again, then
  `src/Agent/Ingest/JsonArrayRecordWriter.cs` and `src/Agent/Evaluation/Evaluator.cs`.** Once
  every record has finished, the rows are written as one array to the output file, the accounts
  to the diagnostics file, and the evaluator scores each row against the record's own expected
  values. This record passes every measured check; the two judge columns read not measured,
  because the judge was not asked for.

Two things to listen for while saying this out loud, both of which are findings if they happen:
needing to search to find the next step, and saying "then it goes through an interface to the
only class that implements it." Three interfaces exist in the library. The composer's has three
implementations in the product. The completion client's and the safety validator's each have one
implementation in the product and a substitute in the tests, which is what lets the suite run the
agent offline and is what earns each of them. Step 3 and step 5 above are the two hops.
