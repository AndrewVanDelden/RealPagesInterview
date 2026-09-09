# Next-Best-Message Agent: design

Rewritten 2026-09-07 from playbook Phase 0 step 1, after the hold-out retrospective. The two
records in `sample.jsonl` are the only evidence any rule below is fitted to. The twelve-record
file `holdout_12.jsonl` is an evaluation set: it is run and reported, never fitted to
(decision D9 in [DECISION_LOG.md](DECISION_LOG.md)). Decisions are one paragraph each in the
decision log; this document holds the problem, the inputs, the rules with their evidence, the
architecture, the evaluation contract, and the numbered assumptions the log cites.

## 1. Problem

**Restatement (step 1).** Given a JSONL file where each line describes one person (a prospect
or a resident of an apartment property), their consent per channel, their ordered channel
preferences, context such as property, dates, timezone, language and stated interests, and the
constraints and thresholds the message must satisfy, produce for each line one `next_message`
(channel, send time, subject, body, call to action, or nothing) and one `next_action` (what the
system does next). The program learns what to do only from the fields of each record. A
labeled `expected` object on a line is the answer key for scoring and is never read to decide.

**Requirements: the verbs of the problem statement (step 2).**

| Verb in the statement | Requirement | Where it lives |
|---|---|---|
| reads the input record | one typed record per line, a bad line is one error row, never a crash | `Ingest/` |
| decides if it should communicate | consent and preferences decide contactability | `Decisions/` |
| decides how to communicate | channel, send time | `Decisions/` |
| decides what to say | subject, body, call to action, in the record's language | `Composition/`, `Safety/` |
| produces output that semantically matches the expected result | one output object per line, scored by the evaluator | `Evaluation/`, `Agent.Cli` |
| learns what to do only from input data | every rule keys on a named input field; no rule keys on an identifier | this document, section 3 |

**Success criterion (step 3).** The statement's own sentence: the output "semantically
matches the expected result." Fields whose vocabulary the input supplies (channel from
`channel_preferences`, the call-to-action type from `constraints.primary_cta`, the language from
`input.language`) are scored exactly. Fields whose vocabulary the input does not supply
(`next_action.type`, the body) are scored semantically, by a deterministic proxy first and a
pinned model judge second (section 6). The number reported is the honest one on the set the
rules were not fitted to.

## 2. Inputs

Inventory of every field in the two samples (step 4). "Required" means a decision cannot be
made without it; presence in both samples is not evidence of requirement, and absence from
both is not evidence a field cannot appear. Class (step 5): R the system reads, C a constraint
it must satisfy, T a threshold it is measured against, O an oracle it must never read to decide.

| Field | Type | Sample 1 | Sample 2 | Required | Class |
|---|---|---|---|---|---|
| `task_id` | string | yes | yes | yes, identity of the row | R |
| `persona` | string | prospect | prospect | no, unknown value uses the generic policy (A8) | R |
| `lifecycle_stage` | string | new | open | no, same | R |
| `consent.email_opt_in`, `sms_opt_in`, `voice_opt_in` | bool | yes | yes | yes, contactability needs them | R |
| `channel_preferences` | ordered list of channel names | [sms, email] | [email, sms] | yes, channel needs it; unknown names are skipped and reported (A3) | R |
| `input.property_name` | string | yes | yes | no, absent means no property fact in the body | R |
| `input.move_date_target` | date | 2026-01-10 | 2026-02-15 | no, absent means unknown horizon (A7) | R |
| `input.last_interaction` | timestamp with offset | yes | yes | no, absent means the reference time alone sets the day (A4) | R |
| `input.timezone` | IANA id | America/Chicago | America/Chicago | no, absent or unknown means UTC and a diagnostic (A6) | R |
| `input.language` | language tag | en | en | no, absent means en and a diagnostic (A13) | R |
| `input.profile.first_name` | string | Taylor | Taylor | no, absent means no name in the greeting | R |
| `input.profile.city_interest` | string | yes | absent | no | R |
| `input.profile.amenity_interest` | list of strings | absent | [pool, fitness] | no | R |
| any other member, at any depth | unknown | none seen | none seen | no; kept, listed in diagnostics, logged per record, never an error (A16) | R |
| `assertions.required_states` | list of state names | 3 | 3 | no; each is earned or reported as not earned (A14) | C |
| `assertions.constraints.no_pii_leak` | bool | true | true | no, absent means not required | C |
| `assertions.constraints.no_sensitive_discrimination` | bool | true | absent | no, same | C |
| `assertions.constraints.include_opt_out_instructions` | bool | true | true | no, same | C |
| `assertions.constraints.primary_cta` | string | book_tour | book_tour | no, absent means the generic call to action (A9) | C |
| `thresholds.p95_latency_ms` | int | 2000 | 2000 | no, absent means not measured | T |
| `thresholds.personalization_score_min` | number | 0.85 | 0.8 | no, same | T |
| `thresholds.reply_classification_f1_min` | number | 0.9 | 0.9 | no; no classifier exists, reported as not measured (A15) | T |
| `thresholds.safety_violations_max` | int | 0 | 0 | no, absent means 0 | T |
| `expected.next_message`, `expected.next_action` | objects | yes | yes | no; a line without them is unscoreable, not an error | O |

Output object, per line, always both members:

```json
{
  "next_message": { "channel": "sms | email | voice | none", "send_at": "ISO-8601 with local offset, or null",
                    "subject": "string or null", "body": "string or null",
                    "cta": { "type": "string", "options": ["..."], "link": "https://..." } },
  "next_action":  { "type": "string", "name": "...", "value": 3, "reason": "..." }
}
```

## 3. Rules and their evidence (step 6)

The two samples differ on: the second channel preference order, sms consent, the move date
(33 versus 68 days after the reference date), the stated interest (city versus amenities),
the chosen channel, the subject (null versus present), the call-to-action payload (options
versus link), the send hour, and the next action. Each rule below names the field it keys on,
its application to both samples, and every other field that would reproduce the same two
values (the confound). Ids and names are labels, not evidence.

| Decision | Rule (keyed on) | Sample 1 | Sample 2 | Confound or gap, and the assumption |
|---|---|---|---|---|
| Communicate | at least one preferred channel is opted in (`consent`, `channel_preferences`) | sms and email on: yes | email on: yes | no negative sample; suppression shape is A2 |
| Channel | first entry of `channel_preferences` that is opted in | [sms, email], sms on: sms | [email, sms], sms off: email | "sms when consented, else email" fits both too; A3 picks preference order because the list is ordered |
| Send day | first day at or after max(reference time, `last_interaction`) in `input.timezone` | last 12-08 09:04 local, ref 12-09: 12-09 | last 12-06 05:30 local, ref 12-09: 12-09 | "last_interaction plus N" needs N=1 and N=3, two constants for two rows; the reference time is A4 |
| Send hour | by channel: sms 09:00, email 10:00 local | 09:00 | 10:00 | voice unseen; minutes unseen; A5 |
| Send slot on a transition day | a slot the zone springs forward across resolves past the gap; one it falls back across resolves to the earlier of its two instants; `schedule.slot` in the diagnostics names which | both samples exact | both samples exact | no zone in the current database transitions across 09:00 or 10:00, so neither branch is reachable from the data; A20, D21 |
| Subject | email only | null | present | A11 |
| Body facts | first name, property, stated interest, horizon cue, opt-out phrase for the channel | all present | all present | A12 |
| Call to action type | `constraints.primary_cta` through a vocabulary table | book_tour: schedule_tour | book_tour: schedule_tour | one pair observed; unknown values pass through, absent is generic (A9) |
| Call to action payload | sms carries numbered options in the body and `cta.options`; email carries `cta.link`, built from the property slug and the catalog's path | options | link | link value unseen beyond one host; A10, A21, D25 |
| Next action | horizon = `move_date_target` minus reference date picks the branch (short or long); the branch reads the catalog row for `persona` and `lifecycle_stage` | prospect/new, 32 days: start_cadence | prospect/open, 68 days: follow_up_in_days 3 | threshold anywhere in (32, 68]; N seen once; absent date; A7 |
| Unknown persona or stage, or a branch no row states | the generic row, and `action_plan.source` in the diagnostics names which of the two fallbacks fired | prospect/new states no long branch | prospect/open states no short branch | A8 |
| Language | body in `input.language`, with no allowlist anywhere; the offline composer holds a template set per language it can serve, English and Spanish, and any other tag is served in English with `locale_applied` false; the model path passes the tag through | en | en | A13, D26 |
| Required states | earned by the step that proves them, recorded in diagnostics; unknown names are reported as not earned | 3 named | 3 named | A14 |

## 4. What the examples cannot tell (step 7) and what was asked (step 8)

Risk register. Each item becomes at least one record in the synthetic set (D6) before it
becomes a branch in code. That set is `synthetic_12.jsonl`: one record per item, two for
item 8 (an unknown timezone, and a send slot across the daylight-saving transition), one
malformed line for item 10, labels written from the assumptions of section 7, run with
`--now 2026-03-07T12:00:00Z`, frozen since 2026-09-08.

1. A record with no consented channel. 2. A persona or stage the samples never named.
3. A record with no move date, or a different date field (lease end, move in, a missed
appointment). 4. A language other than English. 5. A constraint, threshold, or required state
the samples never named, and an absent `primary_cta`. 6. Members at any depth the samples never
showed. 7. A `next_message` spelled with channel `none` and null fields. 8. A timezone the
runtime does not know, and a send slot across a daylight-saving transition. 9. A batch with no
`last_interaction` anywhere, so no field states the reference date. 10. A malformed line
between good lines. 11. A call-to-action vocabulary the samples never showed. 12. A past move
date.

Questions put to the requester on 2026-09-07 and the answers, recorded either way:

- Are rules fitted to the twelve-record file allowed? No. The twelve are unknown to the
  design; the program is built from the two-record file and must be bulletproof for future
  sets that may be harder. (D9)
- Where does the twelve-record file live? In the repo, beside `sample.jsonl`, linked into the
  test output like the sample. (D11)
- How does a run learn its reference time, since no field states the oracle's date? A `--now`
  flag; default is the current UTC time; the documented run against the twelve passes the date
  and says so. (D10)
- Does Phase 0 restart at step 1? Yes; this document is that restart. (D12)
- What is the action-type vocabulary beyond the two samples? No answer available; the catalog
  ships with what the samples show plus `no_op`, and the evaluator scores the rest
  semantically. (A8)
- What are the send minutes and per-stage send days? No answer; not modeled (A5).

## 5. Architecture

The four starting decisions of `~/.agent-rules/ARCHITECTURE.md` stay on their defaults
(decision log S1 to S4): one command-line tool over one library; code owns every decision and a
model writes prose only, choosing the call-to-action type from a code-owned catalog under
constrained decoding; seams exist only at the composer, the completion client, and the safety
validator, each with a real and an offline implementation, and time is a value passed in;
evaluation is part of the deliverable and is built and proven before the product.

```mermaid
flowchart TD
    A[Ingest: one Result per line] --> B[1 Select: contactable channel from consent and preferences]
    B -- none --> S[Suppress: channel none, next_action no_op with reason]
    B -- channel --> C[2 Policy: catalog row by persona and stage, generic fallback]
    C --> D[3 Compose: template or model, in the record's language]
    D --> E[4 Validate: opt-out, PII, fair housing; bounded retry then template]
    E -- still failing --> S2[Suppress: reason composition_failed]
    E -- clean --> F[5 Schedule: channel slot on the first day at or after max of now and last interaction, in the record's timezone]
    F --> G[6 Plan: next_action from the policy row and the horizon]
    S --> H[Emit output plus diagnostics: every decision input, every defaulted field, every fallback]
    S2 --> H
    G --> H
```

| Component | Responsibility | Seam |
|---|---|---|
| `JsonlRecordReader` | one `Result<ProspectCase>` per line; unknown members retained | none |
| `ChannelSelector` | contactable channel or none, from consent and preferences | none |
| `PolicyCatalog` | one data file: action templates and call-to-action vocabulary keyed on persona and stage, with the generic row | none |
| `TemplateMessageComposer`, `OpenAiMessageComposer` | subject, body, call to action, plus the notes saying which of them wrote it (D24) | `IMessageComposer` (real plus offline) |
| `CallToActionCatalog`, `PropertyLink` | the call-to-action vocabulary and the email link (A9, A10, A21) | none |
| `MessageTemplates`, `MessageTemplateCatalog` | one prose set per language the offline composer can serve (A13, D26) | none |
| `OpenAiCompletionClient` | the only network call, on the official SDK, bounded and counted (D27, D28) | `ICompletionClient` (real plus fake) |
| `SemanticJudge` | the two semantic checks, off unless `--judge` (D30) | none; it takes the completion client |
| `SafetyValidator` | opt-out presence, PII, fair-housing terms; violations by category | `ISafetyValidator` (real plus fixed) |
| `SendScheduler` | `send_at` from the reference time, `last_interaction`, timezone, channel | none; time is a parameter |
| `NextActionPlanner` | `next_action` from the policy row and the horizon; `Result` when no rule applies | none |
| `LeasingMessageAgent` | the six numbered steps above, one comment each | none |
| `Evaluator` | the scorecard of section 6 | none |
| `CliRunner` | `--input`, `--output`, `--now`, `--composer`, `--diagnostics`, `--eval-report`, `--log-file`; exit 0, 1, 2 | none |

## 6. Evaluation

Built before the product changes (Sprint 3, landed 2026-09-08) and proven able to fail: the
labels of every set passed as actuals score 100 percent, and one corrupted field per check
scores a failure on that check (playbook step 33; both proofs run in the suite,
`ScorerProofTests`). Every check returns one of three verdicts, passed, failed, or not
measured; a record passes when nothing failed, and not measured never counts as a pass in
the per-check numbers (A15). Scored per record, against the label: channel exact, with the
agent's null message, the oracle's channel `none`, and a null channel as one value; `send_at`
to the day and to the hour in the label's offset; `next_action.type` exact, and beside it the judge's own
verdict on the same field (the semantic judge landed in Sprint 6 under D30: a pinned model,
a pinned rubric, reference-based against the label, off unless `--judge` is passed, and
excluded from a record's pass or fail so it can never overturn a deterministic check); call-to-action type exact against the label's `cta.type` (D13 d);
call-to-action payload presence by channel, options on sms and a link on email; the opt-out
instruction by the one definition the validator enforces (D13 b); body language by a
stop-word detector for English and Spanish, not measured for any other stated language
(D13 c); safety violations within the stated budget, default zero; personalization as coverage
of the first name and the property name over subject plus body (D13 a). Latency is judged
once per batch, a nearest-rank p95 against the strictest stated budget. The scorecard prints
one row per record, a per-check line of passed over measured (the source of every number
in this document and the README), the p95 line, and the overall count. Three sets, all
reported and labeled: `sample.jsonl`, the fitting evidence; `holdout_12.jsonl`, never fitted
to; `synthetic_12.jsonl`, written from section 4 before any decision code changed and never
edited after. `--replay` re-scores an existing output file against its input without running
the agent (D14).

## 7. Assumptions log

Every rule in section 3 cites one of these. Evidence names the input field, the sample, or the
absence that forces the assumption; "configurable" follows playbook step 41: a value is a
constant until a second known value earns a setting.

| # | Assumption | Evidence | Configurable |
|---|---|---|---|
| A1 | Contactable when any preferred channel is opted in | both samples contactable; sample 2 with sms off chose email | no |
| A2 | Suppression emits `next_message` with channel `none` and null fields, and `next_action` `no_op` with a reason | no negative sample; the statement says a message may be "not sent"; the output object must always have both members | no |
| A3 | Channel is the first opted-in entry of `channel_preferences`; an unknown channel name parses as the `Unknown` value, which is never opted in, and the ingest notes name it | sample 1 and 2 as in section 3; the list is ordered by name | no |
| A4 | Send day is the first day at or after max(reference time, `last_interaction`) in the record's timezone; reference time comes from `--now`, default current UTC time | sample 2's date is three days after its last interaction and sample 1's is one day after, so a time outside the record sets the day; answer from the requester, D10 | the flag only |
| A5 | Send hour by channel: sms 09:00, email 10:00, voice 09:00 local; no minutes | sample 1 09:00 sms, sample 2 10:00 email; voice and minutes unseen | no |
| A6 | Unknown or absent timezone resolves as UTC with a diagnostic | both samples America/Chicago; the field is a free string | no |
| A7 | Horizon = `move_date_target` minus the reference date; at most 45 days is short (`start_cadence`), else long (`follow_up_in_days` 3); an absent date is long (no date, no cadence to start); a past date is short (the move is due) | 32 days gave `start_cadence`, 68 gave `follow_up_in_days` 3; 45 falls inside the open interval (32, 68]; it is not the midpoint (50), just a round number in range; absent and past are unseen | no |
| A8 | Unknown persona or stage uses the generic row: A7's rule, diagnostics name the fallback; the catalog names `start_cadence`, `follow_up_in_days`, `no_op`. A row states only the horizon branch a sample showed, so prospect/new has no long branch and prospect/open no short one, and both fall to the generic row rather than to an invented value (A19) | both samples are prospects at `new` and `open`; other values are unseen | the catalog file, `src/Agent/Decisions/ActionCatalog.cs` (D17) |
| A9 | Call-to-action type: `primary_cta` through the vocabulary table (`book_tour` to `schedule_tour`); unknown values pass through unchanged with a diagnostic; absent gives the generic `reply` | one pair in both samples; absence unseen | the catalog file |
| A10 | sms carries numbered reply options in the body and `cta.options`; email carries `cta.link` built as `https://{property slug}.example/{cta path}` | sample 1 options Thu, Fri; sample 2 link `https://oakridge.example/tour` from Oak Ridge Apartments | no |
| A11 | Subject on email only | sample 1 null on sms, sample 2 present on email | no |
| A12 | Body carries first name and property (the two facts the scorer counts, D13 a), stated interest when present, horizon cue when a date exists, and the channel's opt-out phrase | both samples carry name, property, and opt-out; sample 2 carries its amenities; sample 1's body omits its city, so stated interest is composed but not scored | no |
| A13 | Body language is `input.language` and nothing gates on a language allowlist: the model path passes the tag through unchanged, and the template composer holds one set per language it can serve, English and Spanish, with any other tag served in English and the diagnostic `locale_not_applied` | both samples en; the synthetic set's item 4 record is es; the field is a free tag, so other values must not fail (D26) | the template sets |
| A14 | Required states are earned by the step that proves them and recorded in diagnostics; an unknown state name is recorded as not earned, never claimed | three names in both samples; the list is free text; the states map of D42 gives every name in a record's own `required_states` its verdict, `consent_verified` from the consent gate, `fair_housing_check_passed` from the `FairHousing` check alone (D38), `brand_style_applied` from the brand-style validator, and every other name not earned by name; the hold-out's `renewal_offer_loaded` is such a name and stays not earned, because a rule for it would be fitted to the hold-out (D9, A19) | no |
| A15 | `p95_latency_ms` is a nearest-rank p95 over the batch against the strictest stated budget; `personalization_score_min` is fact coverage; a check with no stated threshold, no message to check, or no recorded value is not measured, never passed; `reply_classification_f1_min` and any unknown threshold are not measured | four names in both samples; no classifier is in scope | no |
| A16 | Unknown members at any depth are kept, listed in diagnostics, logged per record, never an error | none seen; the statement promises more cases than the samples show | no |
| A17 | Required members are `task_id`, `consent`, `channel_preferences`; a line missing one is an error row naming it; every other member is optional with its default named in diagnostics | section 2; no decision can be made without these three | no |
| A18 | The model writes prose only and picks the call-to-action type from the catalog under constrained decoding; the template composer is the offline path and the fallback | the statement asks for an autonomous agent, not a model; decision S2 | no |
| A19 | The twelve-record file is an evaluation set; no rule or constant is fitted to it | requester's answer, D9 | no |
| A20 | The channel's slot on a day the zone springs forward across it resolves to the first instant that exists at or after the slot; a slot the zone falls back across, and so reaches twice, resolves to the earlier instant; the diagnostics name which happened | none: both samples are `America/Chicago` at 09:00 and 10:00, and no transition in the current zone database covers those hours, so the branch is unreachable from any observed record; stated over a zone's adjustment rules, proved against custom zones and a sweep of every system zone's transitions (D21) | no |
| A21 | The email link is `https://{slug}.example/{path}`: the slug is the property name lowercased with non-alphanumeric characters removed and a trailing property-type word dropped, and the path comes from the catalog's call-to-action column | sample 2's `https://oakridge.example/tour` from "Oak Ridge Apartments" and `book_tour`; one pair, so "the first two words concatenated" fits it equally and this picks the type-word rule because the other breaks on a one-word or four-word name (D25) | the catalog file |

## 8. Non-goals, quality bar, security

Non-goals: transmitting messages; training models; a user interface; a reply classifier;
per-stage send minutes. Quality bar, self-imposed: strict TDD with the 100 percent gate,
mutation checks on every decision rule, the narration rehearsal before merge, and the honest
number on the unfitted sets reported beside the fitted one. Security: the OpenAI key lives in
`dotnet user-secrets`, scoped to chat completions, never handled by an agent; pricing and
eligibility are never free text; the validator runs on every exit.

**Vendor data retention (step 69, D44).** One external service receives user data: OpenAI's
`/v1/chat/completions`, reached only when `--composer openai` or `--judge` is passed. Read
from the vendor's own platform data page, `developers.openai.com/api/docs/guides/your-data`,
on 2026-09-09. API data is not used to train or improve OpenAI models by default, the position
since 2023-03-01, and the exception is an explicit opt-in. Abuse-monitoring logs for this
endpoint are retained up to 30 days, unless longer retention is required by law or is
reasonably necessary to protect the service or a third party from harm; the stated purpose is
enforcing the usage policies and mitigating harmful uses. Application state is a separate axis
from abuse monitoring, and for this endpoint it is "None" apart from named exceptions, audio
outputs for one hour and prompt-cache tensors expiring within 24 hours; the 30-day
application-state default belongs to the Responses API, not to Chat Completions. Structured
Outputs has no separate retention rule. Two approval-gated controls exist: Modified Abuse
Monitoring excludes customer content from the abuse-monitoring logs, and Zero Data Retention
does the same and additionally forces `store` to false. Both require prior approval by OpenAI
and additional terms, are requested through OpenAI sales, and are configured under Settings,
Organization, Data controls, Data retention; `/v1/chat/completions` is on the ZDR-eligible
list.

Two things the reading did not settle, recorded as absence rather than filled in. The
documentation does not address a request the client abandons at its timeout while the server
completes it: the nearest published fact is that the abuse-monitoring log is generated for the
API feature usage, and nothing narrows or extends the 30-day window for a dropped connection.
That case is not hypothetical here, because D31 measured roughly a third of abandoned attempts
completing on the server after the client walked away. And no tier difference is verified:
`openai.com/enterprise-privacy` returned HTTP 403 and was not read, while the platform data
page draws no tier distinction of its own, its controls being per organization or project and
gated on approval rather than on purchase tier. Not verified is what is recorded here, not
that an enterprise or business tier retains the same way.

The judgment is D44's, and both halves of it are stated rather than the convenient one: the
default is acceptable for this project as it stands, and it is not acceptable for a production
deployment carrying real prospect data. The first half holds on what the prompt actually
contains, which is first name, property name, stated interest, persona, lifecycle stage,
language, and two dates. It carries no phone number, no email address, no unit number, no
renewal offer id, and no free-text note, and that is a property of `BuildUserPrompt` a golden
test already pins. The production path, if a real deployment sends real prospect data, is one
of the two approval-gated controls above.

## 9. Plan

Each sprint cites decisions in the log; one PR per sprint against `dev`.

Phase record, from `~/.agent-rules/PROJECT_PLAYBOOK.md`. The live one is at the top of
[DECISION_LOG.md](DECISION_LOG.md); this is what has been passed and what passed it.

| Phase | Its check | Passed |
|---|---|---|
| 0 Intake | the design doc's assumptions log has an evidence column with no blanks | 2026-09-07, after restarting at step 1 the same day (D12) |
| 1 Environment and agent setup | a fresh clone builds and runs the empty test suite from one documented command | 2026-09-07, CI green on PR #16, with `dev` requiring the `test` check (D8) |
| 2 Scaffold and verification harness | the evaluator scores the golden expected outputs at 100 percent and a deliberately wrong output at less | 2026-09-08 in Sprint 3, both proofs in the suite (`ScorerProofTests`, D13) |
| 3 Deterministic core | the deterministic core passes the synthetic set with every fuzzy component stubbed, and the diagnostics explain every decision | 2026-09-08 in Sprint 5, with the rule for what earns a diagnostics object stated (D18, D22, D23) |
| 4 Fuzzy and external components | with the network disabled, the product completes the full example set using the offline path and the diagnostics say so on every record | 2026-09-08 in Sprint 6, run with outbound HTTPS blocked at the process level; step 60 closed the same day (D31) |
| 5 Safety, security, and compliance | every validator has a passing test, a failing test, and a false-positive test | not passed |

| Sprint | Implements | Proof |
|---|---|---|
| 1 Decisions and gates | this document, the decision log, `holdout_12.jsonl`, CI, branch protection (S1 to S4, D8 to D12) | Phase 0 check: section 7 has no blank evidence cell; Phase 1 check: the `test` check green on the PR |
| 2 Contracts (landed 2026-09-08) | optional members, unknown members retained and logged, per-record diagnostics, `--now`, suppression shape and reason, consent first (D1, D3, D10, the first line of D2) | every record of both files parses to one row and runs to a valid output; diagnostics name every defaulted field and every unknown member |
| 3 Harness (landed 2026-09-08) | evaluator on every field of section 6, scorer proof, synthetic set from section 4, replay mode, one log scope owner (D6, D13 to D16) | scorer scores the labels at 100 percent and a corrupted field below; baseline numbers for all three sets recorded and pinned in the suite |
| 4 Decision core (landed 2026-09-08) | catalog keyed on persona and stage, generic fallback, the `Result` on catalog construction, the planner's decision object in the diagnostics (D2, D17 to D20) | synthetic set through the core with the composer stubbed; diagnostics explain every decision |
| 5 Scheduling (landed 2026-09-08) | reference time, floor, the slot on a transition day, and the schedule in the diagnostics (D4, D21, D22) | property tests green over every system zone at both send hours across every 2026 transition; unknown timezone is a row, not an exit |
| 6 Composition (landed 2026-09-08) | the composer named in the diagnostics, the call-to-action payload and its catalog, language sets, the official SDK bounded and counted, the model prompt inputs and its boundary, the judge (D5, D19, D24 to D30) | all three sets complete on the offline path with outbound HTTPS blocked, and the diagnostics name the composer on every record that has a message |
| 7 Safety and states | earned states, violations by category, false-positive tests (D3) | every validator has a passing, a failing, and a false-positive test |
| 8 Structure and narration | interface removal, orchestrator steps, `docs/NARRATION.md`, final numbers (D7); repoint the `LeasingMessageAgent` comment that cites "section 4" to section 5 | narration delivered without notes; both numbers in the README |

Sprints 2 and 3 were swapped on 2026-09-08 before Sprint 2 started: the harness cannot score a
file it cannot parse, and playbook steps 25 to 27 (nullable domain types, a per-record reader,
the output types) precede step 28 (the evaluator) inside Phase 2.

**Numbers after Sprint 2**, `holdout_12.jsonl` with `--now 2025-12-09T00:00:00-06:00`, the
evaluator as it stood before Sprint 3 (channel, action type, opt-out, call to action against
the constraint, safety, personalization, latency): 12 of 12 rows valid, exit code 0; channel
12 of 12; `next_action.type` 7 of 12, every miss a value the two samples never showed
(`reset_cadence`, `schedule_sms_reminder`, `branch_on_intent`, `start_esign_flow`, and the
long-horizon welcome cadence); 7 of 12 rows pass every check. `sample.jsonl`: 2 of 2. These are
measurements, not targets (D6, D9).

**Numbers after Sprint 3**, the evaluator of section 6 on every field, the template composer,
latency from the CLI run. `holdout_12.jsonl` with `--now 2025-12-09T00:00:00-06:00`: channel
12 of 12, day 7 of 11, hour 5 of 11, action 7 of 12, opt-out 11 of 11, call-to-action type
7 of 11, payload 0 of 11, language 10 of 11, safety 12 of 12, personalization 8 of 8, p95 13 ms
under a 2000 ms budget; 1 of 12 records passes every check (the opt-out record).
`synthetic_12.jsonl` with `--now 2026-03-07T12:00:00Z`: channel 12 of 12, day 10 of 10, hour
10 of 10, action 12 of 12, opt-out 10 of 10, call-to-action type 10 of 10, payload 0 of 10,
language 9 of 10, safety 12 of 12, personalization 9 of 9; 2 of 12 pass; exit code 2 for the
malformed line, by design. `sample.jsonl`: payload 0 of 2, every other check 2 of 2, 0 of 2
pass. What the numbers say: the template composer emits no reply options and no link, so
payload fails on every message until Sprint 6; the Spanish record fails language in template
mode (A13); the five action misses are vocabulary the samples never showed; of the four
call-to-action misses, two are vocabulary (`reschedule`, `intent_capture`) and two are records
with no `primary_cta` whose labels still carry a type; the day misses are the oracle's
per-stage send days and the hour misses add its per-stage hours and minutes (A5).
Personalization reads 1.00 on every message because the template inserts both scored facts by
construction: the check can fail (the proof corrupts it) but it cannot fail this composer, and
the body judge of Sprint 6 is the check with teeth for content. Measurements, not targets
(D6, D9); `BaselineNumbersTests` pins every tally so a drop fails CI and a rise is recorded
here and in the README together.

**Numbers after Sprint 4** are the Sprint 3 numbers, every tally unchanged on all three sets,
and that is the result the sprint set out to get. The catalog reproduces A7's rule exactly:
its generic row is that rule, and the two keyed rows state only the branch a sample showed,
so no record's action moved. What the sprint adds is the account of the decision. On
`holdout_12.jsonl`, `action_plan.source` reads `catalog_row` on 3 records,
`generic_row_no_branch` on 1, `generic_row_no_match` on 7, and is null on the 1 record the
consent gate suppressed before the planner ran. Against the action check's 7 of 12: all 3
`catalog_row` records pass, the suppressed record passes, 3 of the 7 `generic_row_no_match`
records pass, and every one of the 5 misses used the generic row, 4 of them because no row
exists for that persona and stage and 1 because the row that matched states no action for
that horizon branch. On `synthetic_12.jsonl`: `catalog_row` on 7, `generic_row_no_branch` on
2, `generic_row_no_match` on 1, null on 2, and the action check is 12 of 12, so the generic
row answers those three records correctly. Measurements, not targets (D6, D9);
`BaselineNumbersTests` still pins every tally.

**Numbers after Sprint 5** are the Sprint 3 numbers again, every tally unchanged on all three
sets. Nothing was expected to move: the slot rule (A20) only reaches a record whose zone
transitions across 09:00 or 10:00, and the current zone database has none, so every record on
every set resolves its slot exactly. What the sprint adds is the account of the send. On
`holdout_12.jsonl`, `schedule` reads `reference_time` / `America/Chicago` / `exact` on 11
records and null on the 1 the consent gate suppressed. On `synthetic_12.jsonl`: 8 records
`reference_time` / `America/Chicago` / `exact`; the unknown-timezone record (item 8) reads
`UTC`, which is A6 visible in the diagnostics rather than inferred from a send time; the
daylight-saving record (item 8's second case) reads `last_interaction`, since its last
interaction is after the run's reference time, and `exact`, because 10:00 is past that day's
transition; and the 2 suppressed records read null. The day and hour checks are unchanged
on both sets, 7 of 11 and 5 of 11 on the hold-out and 10 of 10 and 10 of 10 on the synthetic
set, so no send moved. Measurements, not targets (D6, D9);
`BaselineNumbersTests` still pins every tally.

**Numbers after Sprint 6**, the template composer, every set run with outbound HTTPS blocked
so the offline path is the only one that could have answered. `sample.jsonl` with `--now
2025-12-09T00:00:00-06:00`: every check 2 of 2 and 2 of 2 records pass, where Sprint 5 read
payload 0 of 2 and 0 of 2 passing. `holdout_12.jsonl` with the same reference time: channel
12 of 12, day 7 of 11, hour 5 of 11, action 7 of 12, opt-out 11 of 11, call-to-action type
7 of 11, payload 11 of 11 (was 0 of 11), language 11 of 11 (was 10 of 11), safety 12 of 12,
personalization 8 of 8, p95 18 ms under a 2000 ms budget; 4 of 12 records pass every check,
where Sprint 5 read 1 of 12. `synthetic_12.jsonl` with `--now 2026-03-07T12:00:00Z`: every
check perfect, payload 10 of 10 (was 0 of 10) and language 10 of 10 (was 9 of 10), and 12 of
12 records pass, where Sprint 5 read 2 of 12; exit code 2 for the malformed line, by design.
The judge's two checks read 0 of 0 on every one of these runs, which is what off means (D30).

What moved and why. Payload was the one check no message could pass before this sprint,
because the template composer emitted neither options nor a link; D25 gives it both from one
catalog, and A21's slug rule turns the record's own property name into the host. Language
moved on both evaluation sets from one rule earned on the synthetic set's Spanish record
(D26), and the hold-out's Spanish record passing is that rule generalizing rather than a rule
fitted to it (D9). Nothing else moved: day, hour, action and call-to-action type are the same
tallies as Sprint 3, and their misses are the same ones, the oracle's per-stage send days and
hours (A5) and vocabulary the two samples never showed (A8, A9).

What the numbers still do not say. The body has no check with teeth on these runs: the
personalization proxy reads 1.00 on every message because the template inserts both scored
facts by construction, and the judge, which is the check with teeth, is off. A run with
`--judge` measures it, and the honest comparison of the offline and the model paths (playbook
step 60) is a live run, recorded here when it is made. Measurements, not targets (D6, D9);
`BaselineNumbersTests` pins every tally in this paragraph.

**Numbers after the step 60 run**, `--composer openai` against all three sets on 2026-09-08,
the first live model run this project has made. The model wrote nothing: on all 23 records that
have a message, `diagnostics.composition` reads `composer: template` with `attempts: 3`, which
is the compose-validate loop's two model attempts and then the fallback. Every model call was
abandoned at its timeout, 46 calls and 92 HTTP requests in total, because the strictest stated
`p95_latency_ms` is 2000 ms, the client splits that into two 1000 ms attempts (D28), and a
completion of this size does not come back in 1000 ms. Every per-check tally is therefore
identical to the offline run above, since the same composer wrote every message: 2 of 2, 4 of
12 and 12 of 12 records passing, with exit codes 0, 0 and 2. The one number that moved is
latency: the batch p95 is 5704 ms, 5698 ms and 5699 ms against a 2000 ms budget, so the p95
check fails on all three sets where it passes at 18 ms offline. That is the cost of trying, and
it is what the fallback path is for: no record was lost, no output row is missing, and the
diagnostics name the fallback on every one.

What this settles and what it does not. It settles step 60's choice, which is D31: the offline
path is what this product uses on these sets, on the measurement rather than on preference. It
does not settle whether the model writes better messages than the template, because on this
data it never got to write one; that comparison needs the open question in D31 answered first.
It does settle that the degradation path works on real network failures rather than only on
fabricated ones: the timeout, the retry, the bounded compose loop, the fallback and the
diagnostics all behaved as the tests said they would, against the live API.

**What the step 60 run cost**, read from the vendor's own usage page rather than estimated.
About $0.004 for the batch: `gpt-4o-mini` input $0.002 and output $0.002, at the published
$0.15 and $0.60 per million tokens, against roughly 12,100 tokens. The interesting number is
the request count. The client made 92 HTTP attempts, 46 model calls each retried once, and the
vendor recorded about 30 requests: abandoning a call at its 1000 ms timeout stops roughly two
thirds of them from ever becoming billable requests, while the remaining third complete on the
server after the client has walked away and are billed in full, output tokens included. So a
timeout is a partial refund, not a free abort, and a run that times out on every record still
pays for about a third of what it asked for. The estimate made before the run assumed every
attempt would bill its input, which was high on both counts. Per record, this is about
$0.00017 for a record that produced no model text at all.

