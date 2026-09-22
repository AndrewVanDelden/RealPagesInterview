# Retrospective, the owner's 50-record set, 2026-09-21

Playbook step 101 for the run that closed Phase 9's step 99: `runs\step99e`, the owner's 50-line
`TrueTest.jsonl`, unseen before it was run, scored against the owner's answer key
`probe50answerkey.md`. Section 1 is the one-page summary. Section 2 explains every failure by the
input field, the rule and the assumption that caused it (step 100). Section 3 names the playbook step
that would have caught each one (step 101). Section 4 lists the root causes step 102 would turn
into decisions. None is scheduled, because the code is frozen for delivery. The earlier
retrospective, on the 12-record hold-out, is [RETROSPECTIVE_2026-09-06.md](RETROSPECTIVE_2026-09-06.md).

## 1. Summary

**What was run.** One command, with everything on: `--composer openai --judge`, diagnostics, review
queue, evaluation report, log and run report. `--now 2026-03-09T18:00:00-06:00` is the frame the key's
labels describe. 38 of its 41 dated labels expect a Tuesday 2026-03-10 send, and record 6's missed
tour is 15:00 that Monday. The run exited 0, and all 50 records were answered.

| Measure | Result |
|---|---|
| Against the answer key, by its own rule (channel, send instant, call-to-action type, action type, body condition) | **39 of 50**; 40 if the key's Discussion clause passes record 36 |
| The program's own evaluator, which also compares every payload member, every action value and a model judge's reading of the body | 22 of 50 |
| Channel, safety, opt-out, call-to-action type | 50/50, 50/50, 41/41, 41/41 |
| Language, personalization | 38/38, 35/35 |
| Records correctly left unmessaged (no consent, contact rules, a lease already ended, a tour not yet missed) | 9 of 9 |
| Leaks the key greps for (a Social Security number, card digits, a phone, an email, year 0001, `Hi ,`) | 0 in all seven output files |
| p95 latency, model path | 4,327 ms against a 2,000 ms budget: **fail** |
| Model spend | 42 product calls, 22,013 input and 3,222 output tokens; 50 judge calls |

**What generalized.** Every decision that code owns from an explicit input held on unseen data:
consent and channel, the contact rules that stop a message whatever consent says, the five safety
gates, input cleaning, the language fallback, and the timezone arithmetic, including half-hour
offsets and a daylight-saving boundary. No record leaked a field the key forbids.

**What did not.** Rules fitted to a handful of labels:
- the send-slot table's minute values;
- the horizon bands at their extremes;
- the tour-slot lead day;
- a schedule floor that trusts an inconsistent input.

The model path misses its latency budget, and the template path meets it.

**The eleven failures in three groups.**
- Eight product findings no frame explains: send slots on 12, 17, 18 and 32; a past move date on 29;
  a 22-month horizon on 30; a future `last_interaction` on 31; and the tour-slot lead day, which is
  also 22 of the 24 payload mismatches the program's own evaluator reports.
- Two of the key's Discussion rows, where its stated condition is met but its strict rule is not: 8
  and 36.
- Two no single reference time can satisfy: 33 and 39 want a Monday 09:00 send, which is before the
  Monday-evening frame the other 38 labels fix.

**Why 22 and 39 differ.** The key compares types, and the program's evaluator compares values. The
tour-slot finding alone fails 22 payload checks. `follow_up_in_days` 2 against a label of 3 fails an
action check the key's type-only rule passes. The judge also misreads weekdays in several of its
body verdicts: `eval.txt` has it calling March 11 and 12 a Thursday and Friday. Section 2 reads
every failure against the key.

## 2. Every failure, by its cause

The reference time is 2026-03-09T18:00:00-06:00. Record numbers are line numbers in `TrueTest.jsonl`.
Code locations are under `src/Agent/`.

| # | Record | Output against the key | Input field | Rule and assumption |
|---|---|---|---|---|
| 12 | `resident_consent_missing_sms_key` | email at 10:00; key 09:00 | `consent` without `sms_opt_in`, so email; stage `renewal_undecided` | `Decisions/SendSlotTable.cs` has a `renewal_undecided` row for sms only, so email takes the channel hour, 10:00 (A5) |
| 17 | `prospect_profile_familial_status` | sms at 09:20; key 09:00 | stage `open`, a move date stated | the prospect/open/sms row is 09:20 on every branch (A5). The labels split it: 17, 18 and 29, with a move date, want 09:00; 40 to 43, without one, want 09:20 |
| 18 | `prospect_notes_mention_religion_and_origin` | sms at 09:20; key 09:00 | as 17 | as 17. The body names neither the mosque nor the country, which is the part the key tests |
| 32 | `prospect_last_interaction_year_0001` | sms at 09:00; key 09:05 | no move date, channel sms | the 09:05 row is keyed to email with no move date, so sms takes its channel hour (A5). No year 0001 value reaches any output |
| 29 | `prospect_move_date_in_past` | `follow_up_in_days` 2 at 09:20; key `reset_cadence` at 09:00; the body says "before your move in November 2025" | `move_date_target` 2025-11-01, 128 days past | `Decisions/NextActionPlanner.cs` sends a past date to the no-move-date branch (A7). The model is handed the date as data, and the past-date guard in `Composition/OpenAiMessageComposer.cs` covers email only. The send time is the prospect/open row, as 17 |
| 30 | `prospect_move_date_far_future` | `follow_up_in_days` 3; key 14 | `move_date_target` 2028-01-15, 677 days out | every horizon over 60 days is one long branch with one value (A7) |
| 31 | `prospect_last_interaction_in_future` | 2026-04-01T09:00-06:00 with April tour dates; key 2026-03-10T09:00-06:00 | `last_interaction` 2026-04-01, three weeks after the reference time | the floor is the later of the reference time and the last interaction (A4). The diagnostics record `floor: last_interaction`, but no ingest note calls the value inconsistent, which the key also requires |
| Tour slots | 22 sms and voice tour invitations | Wednesday 03-11 and Thursday 03-12; key Thursday 03-12 and Friday 03-13 | the reference time | `Decisions/TourSlots.cs` counts its two-day lead from the run's local date, not the send date. The hold-out and `synthetic_v2.jsonl` labels fit the reference date, and this key fits the send date (D108). This is the finding behind 22 of the evaluator's 24 payload failures. The other two are record 8's single option and record 31's April dates |
| 8 | `prospect_voice_only_consent` | voice, "press 9", `start_cadence`; key `follow_up_in_days` 3 | channel voice, move date 40 days out | the action catalog does not read the channel, so a short horizon starts the cadence (A7, A8). The key lists this as a Discussion row |
| 36 | `resident_timezone_unrecognized` | 2026-03-11T09:30Z; key 2026-03-10T09:30-06:00 | `timezone` "Mars/Olympus_Mons", no state in the city | UTC fallback, stated in the ingest notes (A6). UTC moves the local calendar day. The key says a stated UTC fallback passes, and its strict rule fails it |
| 33 | `prospect_dst_boundary_chicago` | 2026-03-10T09:00-05:00; key 2026-03-09T09:00-05:00 | none: the offset is right | the key's instant is ten hours before the reference time the other labels fix, so no single `--now` passes it with them |
| 39 | `prospect_late_night_interaction` | 2026-03-10T09:00-06:00; key 2026-03-09T09:00-06:00 | none | as 33 |

The p95 failure is the model path's cost. A timeout short enough to meet 2,000 ms returns no
message: 14 of 48 calls at that budget in the first step 99 run. So the run gives each call 60
seconds and reports the p95 against the records' own budget. The template path's p95 is 19 ms on
`synthetic_12.jsonl`.

## 3. The playbook step that would have caught each failure (step 101)

| Failure | Step | Pillar |
|---|---|---|
| Send slots, 12, 17, 18, 32 | 6: name every input a difference is confounded with (the move date for 17 against 40); 41: where examples under-determine a rule, record the candidate rules as an assumption | VF: a rule read off examples fitted to too few of them |
| Past move date, 29 | 44: every boundary the synthetic set exposed; 55: the model needs the fact that the date is past, not only the date | VF |
| Far horizon, 30 | 7: what the examples cannot tell you (the longest labeled horizon in any fitted set is 100 days, against this record's 677); 9: a boundary-date case | VF |
| Future `last_interaction`, 31 | 26: validate each record and report a failure per record; 44: boundaries | HB: an inconsistent external value was trusted and not reported |
| Tour-slot lead day | 6 and 41: two rules (reference date, send date) fit the fitted labels equally, so they were one open question, not one rule | VF: two rules that fit the same examples were stated as one |
| 8, voice action | 6: the channel was never tested as a driver of the action | none: the key calls it a Discussion row |
| 36, UTC fallback | 8: ask the requester what an unrecognized zone should do | none: the key's own text allows it |
| 33, 39 | 3 and 8: the key states the day and not the hour; the frame had to be read off the labels | none: a label set no single frame satisfies |
| p95 | 49 and 60: the budget and the implementation choice, stated with a reason | none: a recorded trade-off |
| A comment that contradicted its code: `Decisions/HorizonBranch.cs` said a past date is short after D117 made it no-move-date; fixed in this pass | 98: narrate the code aloud and check it against itself | HSC |

Every failure names an existing step, so step 104 has nothing to add. The playbook stays as it
is, as the owner decided at D79.

## 4. The root causes step 102 would decide

Each is a question with its options, none taken. Fitting any of them to this key makes
`TrueTest.jsonl` training data (step 103). An honest number would then need another unseen set.

1. **What keys a send slot?** (a) Add whether a move date is stated to the slot key, which separates
   17, 18 and 29 from 40 to 43; (b) key the 09:05 row on branch, not channel; (c) keep the table and
   record the key's labels as a second, disagreeing source. Recommendation: (a) and (b), each tested
   against the hold-out and `synthetic_v2.jsonl` first. Scopes A5, `SendSlotTable`.
2. **What does a past move date mean?** (a) Its own branch, answered `reset_cadence`, and the
   composer told the date is past on every channel; (b) keep the no-move-date branch and fix only the
   prose. Recommendation: (a). Scopes A7, the planner, the prompt.
3. **Is there a horizon beyond long?** (a) A third band past a stated number of days, answered with a
   longer follow-up; (b) keep one long value. Recommendation: (a) only once a second labeled record
   states the band's edge, since one label fixes no boundary. Scopes A7.
4. **What does a `last_interaction` after the reference time do?** (a) Floor on the reference time
   and write an ingest note naming the inconsistency; (b) keep the floor and add only the note.
   Recommendation: (a), which is the key's stated condition. Scopes A4, ingest notes.
5. **Which date does a tour lead count from?** (a) The send date, as this key does; (b) the reference
   date, as the fitted labels do. Recommendation: ask the requester. The two labeled sources
   disagree, and either fit fails the other. Scopes D108, `TourSlots`.
6. **Does the channel change the action?** Recommendation: decline until the requester answers the
   key's Discussion row. Scopes A8.
7. **The model path's latency.** Options: a faster model, streaming, or the template path when the
   budget binds. Recommendation: none from one run. Scopes D70.
