# How the next-best-message agent works, out loud

Filled from Appendix B of `~/.agent-rules/PROJECT_PLAYBOOK.md`. The script below is 193 words,
about one minute spoken, and answers the seven questions in order. Rules while speaking: no
pattern names, no class names, one file name at most, and walk one record rather than the parts.
The numbers are updated after every run that changes them.

## One minute

It reads leasing records and writes one row for each: whether to message, on which channel, when,
what to say, and what happens next.

Every field is cleaned first, then six steps run in one file. Pick the first channel the person
prefers and has consented to, and check the rules that stop contact whatever consent says. Plan the
next action from who they are, their stage, and how far off their move is. Schedule it in their
timezone. Write the message: code picks the call to action, its link, the reply options and the
property facts, and a language model writes only the sentences around them. Run five safety checks.
Emit the row with a reason for every decision.

If the model fails or a draft is unsafe, a template writes it. If no rule fits, a fallback answers
and a person reviews it.

On the hold-out, which the rules were fitted to, every decision is right. On the owner's own 50
records, unseen before one run and scored against their answer key, 39 of 50 pass. That is the
honest number, and each of the eleven misses has a named cause.

Last model result, 2026-09-17, `runs\step99e`: the owner's `TrueTest.jsonl` with the model and the
judge at the answer key's frame, 22 of 50 on the program's own checks and 39 of 50 against the key
([RETROSPECTIVE_2026-09-21.md](RETROSPECTIVE_2026-09-21.md)). Template results, pinned by
`BaselineNumbersTests` at `bbeaf57`, reference times as documented: `sample.jsonl` 2 of 2,
`holdout_12.jsonl` 12 of 12, `synthetic_12.jsonl` 5 of 13 and `synthetic_v2.jsonl` 26 of 30, each
malformed line an error row, and zero safety violations on all four.

## Curveballs I have rehearsed

- **A timezone the runtime does not know.** Today it uses the zone of the state the record's city
  names, and UTC when no state is named, and the per-record account says which zone it used. The
  zone is chosen before step 1b and used by steps 2 and 3. Proof: `synthetic_08_unknown_timezone`,
  which names no city and schedules in UTC, and `v2_unknown_timezone`, whose "Dallas, TX" is sent in
  Central time.
- **A required state the program has no check for.** Any name outside the four it checks is
  recorded by name as having no check, neither claimed nor failed. Step 6 owns it.
  `renewal_offer_loaded` was such a name; it is now earned by finding the record's renewal offer in
  the property data, the stand-in for the property management system. Proof: the hold-out's three
  renewal records, earned with `--property-data` and not earned without it.
- **A record that asks for quiet hours.** Today it gets its stage's send time, or the channel's
  hour when its stage has none, because no quiet-hours window is modeled and no input file has
  ever carried one. Step 3 owns it. Fix: read the window off the record and move the slot to the
  first time inside it. Proof: a record carrying the window whose send time moves and whose send
  hour still scores.
- **Steering stated as meaning rather than as a listed term.** A body reading "perfect for young
  professionals" passes the fair-housing check today, because that check matches terms and spans,
  not intent, and the message ships. Step 5 owns it. Fix: put the body through the semantic judge
  as a gate rather than only as a report line. Proof: that body going from a clean row to a review
  queue row.
- **A persona and lifecycle stage no row covers.** Today the generic row answers, the message
  goes out, and the record gets a review-queue row naming the missing pair, so a person sees it
  without opening the diagnostics. On `synthetic_v2.jsonl` that is one record. Step 2
  owns it. Fix: add the row when a labeled training record shows the pair. Proof:
  `synthetic_02_unseen_persona_and_stage`, which the generic row answers correctly and which is
  still queued, because a correct guess is still a guess.

## The rehearsal: one record, file by file

`prospect_welcome_day0`, the first line of `sample.jsonl`, at `--now 2025-12-09T00:00:00-06:00`.
Every value below is from a template run on 2026-09-21 of `dev` at `bbeaf57`, with the diagnostics
file, the review queue and the evaluation report switched on.

- **`src/Agent.Cli/Program.cs` into `src/Agent.Cli/CliRunner.cs`.** The flags are parsed and
  every component is built once. Every record starts as soon as its line is read, each inside its
  own log scope carrying its task id, and the rows are still written in input order; nothing
  downstream passes the id or opens a second scope. Before this record runs, the same file cleans
  every field, and nothing needed changing, so the cleaned list is empty. It then notes what the
  record left out, thirteen paths from the unit to the opt-out date, and no member the program does
  not know, so the unknown list is empty.
- **`src/Agent/Ingest/JsonlRecordReader.cs`.** Line 1 is read when the batch asks for it and
  becomes one parsed record. It carries the two required members, the task id and the channel
  preferences, and a consent object, and that is all this file decides.
- **Step 1, `src/Agent/Decisions/ChannelSelector.cs`.** The preference order is SMS then email;
  consent covers both, so the first one wins and the channel is SMS. Reaching this step is what
  earns the consent state, and nothing later asks the question again.
- **Step 1b, `src/Agent/Decisions/ContactRules.cs`.** No opt-out is on record, the persona is a
  prospect with no stated age, and the stage is new, so no rule stops contact and the record goes on
  to be planned.
- **Step 2, `src/Agent/Common/TimeZones.cs`, then `src/Agent/Decisions/NextActionPlanner.cs` and
  `src/Agent/Decisions/ActionCatalog.cs`.** The reference time in Chicago is the local date
  2025-12-09. The move date is 2026-01-10, so the horizon is 32 days. At most 60 days is short, so
  the branch is short. The catalog has a row for a new prospect, and that row states the short
  branch, so the action is start a cadence named `prospect_welcome_short_horizon`, and the account
  records that a catalog row answered rather than the generic fallback.
- **Step 3, `src/Agent/Decisions/SendScheduler.cs` and `src/Agent/Decisions/SendSlotTable.cs`, then
  `src/Agent/Decisions/TourSlots.cs`.** The last interaction, 2025-12-08T15:04Z, is before the
  reference time, so the floor is the reference time and that is recorded. The slot table has no
  row for a new prospect on SMS, so the send is the channel's hour, 09:00, and the account records
  that the channel default answered. The first 09:00 at or after the floor in Chicago is
  2025-12-09, and the zone reaches that wall time exactly once, so the slot is recorded as exact and
  the send time is 2025-12-09T09:00:00-06:00. Scheduling comes before composing because a tour
  invitation's reply options never fall on or before the send date: two days after the run's date,
  Tuesday 2025-12-09, is Thursday, after the send date, the property data holds no calendar, so Monday to Saturday are open, and the two tour
  slots are Thursday and Friday at 10:00 in Chicago.
- **Step 4, `src/Agent/Safety/ValidatingMessageComposer.cs` wrapping
  `src/Agent/Composition/TemplateMessageComposer.cs`.** The record's language is English, so the
  English prose set is used and the account records that the locale was applied. The record's
  call to action, book a tour, maps to the wire type `schedule_tour` in the call-to-action table,
  which holds the type, the email link path, the purpose the model is told, and the kinds of
  property fact that serve it. The reply options are the two tour slots, written by
  `src/Agent/Composition/TourSlotText.cs` as "Dec 11, 2025, 10:00 AM" and "Dec 12, 2025, 10:00 AM",
  the same in every language, and the link stays null because this is SMS. The body comes back as
  "Hi Taylor! Welcome to Oak Ridge Apartments. We heard you're looking in Richardson, TX. Reply to
  book a tour. Reply 1 for Dec 11, 2025, 10:00 AM; 2 for Dec 12, 2025, 10:00 AM. Reply STOP to opt
  out." The wrapper validates that draft once, it is clean, the attempt count is 1, and the verdict
  travels out with the message.
- **Step 5, `src/Agent/Safety/DraftValidation.cs`, `src/Agent/Safety/SafetyValidator.cs` and
  `src/Agent/Safety/BrandStyleValidator.cs`.** This is the orchestrator's own gate. It asks
  whether the verdict the wrapper sent along answers its own question: the same validator, the
  same draft, this record's constraints. It does, and no check reads the send time, so no second
  validation runs; a draft from anywhere else would be validated here. All five checks passed, the
  last being that no offer is stated which the property data does not hold, the violation count is 0, and the fair-housing state is that one check's own verdict rather than
  "nothing went wrong anywhere". All three brand rules hold, so the failed-rule list is empty.
- **Step 6, back in `src/Agent/Orchestration/LeasingMessageAgent.cs`.** The result goes back to
  the batch: the row, with channel SMS, the send time above, the body above and the action above,
  and beside it the account of every decision, all three states earned, zero violations, no
  suppression, the plan, the schedule and the composition.
- **`src/Agent.Cli/RecordFold.cs`, then `src/Agent/Ingest/JsonArrayRecordWriter.cs` and
  `src/Agent/Evaluation/Evaluator.cs`.** As soon as every earlier record is done, this record's
  row is written to the output file and its account to the diagnostics file, and it is scored
  against its own expected values. It gets no review-queue row, because a catalog row chose its
  action, no contact rule handed it to a person, and the safety gate refused nothing. It passes every measured check; the two judge
  columns read not measured, because the judge was not asked for.

Two things to listen for while saying this out loud, both of which are findings if they happen:
needing to search to find the next step, and saying "then it goes through an interface to the
only class that implements it." Four interfaces exist in the library. The composer's has three
implementations in the product. The completion client's, the safety validator's and the judge's
each have one implementation in the product and a substitute in the tests, which is what lets the suite run the
agent offline and is what earns each of them. Step 4 and step 5 above are the two hops.
