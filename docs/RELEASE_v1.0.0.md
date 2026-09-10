# Release v1.0.0

Tagged at `ccf3c5c`, the PR #29 merge into `dev`. Playbook step 97: the narrative a reviewer reads.
Where it is published is D78.

## Problem

A leasing team needs a decision for each prospect or resident record: whether to message them, on
which channel, when, what the message says, and what the next action is. Input is JSONL. Each
record carries consent, channel preferences, persona, lifecycle stage and, on the evaluation sets,
the label the customer wrote.

## Boundary

Code owns every reproducible decision: the channel, the next action, the send time and the safety
gate. A language model writes prose only when `--composer openai` is passed, and it picks a
call-to-action type from a list the code hands it. The default template composer makes no network
call and needs no key. Every rule is fitted to `sample.jsonl`, two records, and nothing else;
`holdout_12.jsonl` and `synthetic_12.jsonl` are run and reported, never fitted to (D9, D6).

## Pipeline

`LeasingMessageAgent.RunAsync` runs six steps per record, in this order (D59): pick the first
channel the record's consent opts into, or suppress; plan the next action from the catalog row for
its persona and stage and the days to its move date, with a generic row as the named fallback;
compose in the record's language, retrying once only after a safety rejection and then falling back
to the template (D34); schedule on the first channel slot at or after the later of `--now` and the
last interaction, in the record's timezone; validate the finished message against four hard gates,
opt-out, Social Security number, long digit run and fair housing (D39); emit the row plus
diagnostics saying how each decision was reached. `CliRunner` runs up to four records at once (D37)
and writes in input order. A line that does not parse becomes one failure row and exit code 2,
never an abort.

## Evaluation

At this tag, template composer, reference times as documented:

| Set | Overall | Checks below full |
|---|---|---|
| `sample.jsonl` | 2 of 2 | none |
| `holdout_12.jsonl` | 4 of 12 | send day 7/11, send hour 5/11, action 7/12, call-to-action type 7/11 |
| `synthetic_12.jsonl` | 12 of 13 | none; line 11 is malformed by design, an `ERROR` row (D71) |

Zero safety violations and an empty review queue on all three. The hold-out misses are the
oracle's per-stage send days and hours (A5) and vocabulary the two samples never showed (A8, A9).
The model path scored 12 of 13 on the synthetic set on each of three runs, at p95 2,318 to 4,508 ms
against the records' 2,000 ms budget, and beats the template on no scored check
([VARIANCE.md](VARIANCE.md)). 650 tests, 100 percent line, branch and method coverage, with
`BaselineNumbersTests` pinning every tally.

## One bug found and fixed

D66: model spend vanished on exactly the records that failed. Token counts rode
`CompositionNotes`, which is null on every record with no message, and `ValidatingMessageComposer`
dropped its accumulated cost when the fallback produced nothing or the draft was refused. A record
that made two model calls and ended with no message reported no call at all, and the batch total
lost the same tokens. The fix moved both spend counts, tokens and network retries, onto
`ComposeOutcome` and out to `AgentDiagnostics`, beside `composition` rather than inside it, so a
null `composition` no longer takes them with it.

## Open questions

[DESIGN.md](DESIGN.md) section 4 lists nine the data could not answer. The ones that move a
number: the oracle's per-stage send days and hours (A5); the action and call-to-action vocabulary
beyond the two samples (A8, A9); where in (32, 68] days the horizon threshold sits (A7); whether the
model writes better messages than the template, which only the judge could show and no committed
scorecard ran with `--judge`; and whether a live model can meet the records' 2,000 ms p95 (A22).

## Human review, playbook step 94

The files carrying the compliance and security load, read by hand by the owner:

1. `src/Agent/Safety/SafetyValidator.cs`: the four gates, the fair-housing term and span lists, the
   Social Security number and digit-run patterns.
2. `src/Agent/Safety/SafetyTextNormalizer.cs`: what the text is normalized to before matching.
3. `src/Agent/Safety/OptOutInstructions.cs`: the one opt-out definition, shared by the validator
   and the scorer.
4. `src/Agent/Safety/ValidatingMessageComposer.cs`: the retry only after a safety rejection, and
   the fallback validated like any draft.
5. `src/Agent/Decisions/ChannelSelector.cs`: the consent gate; an unknown channel is never opted in.
6. `src/Agent/Orchestration/LeasingMessageAgent.cs`: step 5 gates the finished message whatever
   composed it; the three suppression paths.
7. `src/Agent/Composition/OpenAiMessageComposer.cs`: which record fields reach the vendor, and the
   `<prospect_data>` delimiting.
8. `src/Agent/Common/ExceptionFormatting.cs`: the redaction that keeps prospect text out of logs
   and error rows.
9. `src/Agent.Cli/CliRunner.cs`, the two key reads near lines 690 and 721: check that the key
   comes from configuration only and reaches no log line or error text.

## Phase 8 check

From a worktree at `v1.0.0` on 2026-09-10: `dotnet build` 0 warnings, 0 errors; `.\test.ps1` exit
0; the README run command exit 0; `sample.jsonl` at `Overall: 2/2 passed`; the runbook's hold-out
run exit 0 at `Overall: 4/12 passed`, its synthetic run exit 2 at `Overall: 12/13 passed`, its log
line present in `run.log` under `TaskId=prospect_welcome_day0`, and its replay exit 0 with the
same tallies and Safety 0/0.

## Exceptions on record

D74: the repository stays public with the assignment's text and data. D75: `AGENTS.md` stays at
150 lines, against step 96's "under 150". Step 93's second reviewer, Gemini, did not run; the owner
marked the step complete on 2026-09-10.
