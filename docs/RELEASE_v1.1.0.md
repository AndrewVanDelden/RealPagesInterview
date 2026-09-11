# Release v1.1.0

Tagged at `6e05adb`, the PR #33 merge into `dev`, Sprint 15's decisions D81 to D90. Playbook step
97: the narrative a reviewer reads. Published on GitHub from this file, as D79 did for `v1.0.0`;
the tag is D91.

## Problem

Unchanged from [RELEASE_v1.0.0.md](RELEASE_v1.0.0.md): for each prospect or resident record,
decide whether to message them, on which channel, when, what the message says, and what the next
action is.

## Boundary

Code still owns every reproducible decision and the model still writes prose only. What moved:
the rules the planner and scheduler use are the compiled defaults or a file passed with
`--rules`, refused whole with one line per bad row before any record runs (D82). The hold-out is
now training data, fitted to, and a set written blind to the code, `synthetic_v2.jsonl`, carries
the honest number (D81).

## Pipeline

The six steps per record are unchanged in order. Three things changed around them: a record
answered by the generic row gets a review-queue row naming the missing persona and stage (D83);
a draft is validated once, and the orchestrator reuses that verdict when it answers its own
question (D84); and the input is read and the output written one record at a time, at most four
records ahead of the writer, so memory stays flat as the batch grows (D86).

## Evaluation

At this tag, template composer, reference times as documented:

| Set | Overall | Checks below full |
|---|---|---|
| `sample.jsonl` | 2 of 2 | none |
| `holdout_12.jsonl` | 12 of 12, fitted | none; training data since D81 |
| `synthetic_12.jsonl` | 12 of 13 | none; line 11 is malformed by design, an `ERROR` row |
| `synthetic_v2.jsonl` | 13 of 30 | channel 25/29, send day 24/26, send hour 19/26, action 17/29, call-to-action type 23/24, payload 24/25 |

Safety passed on every scored record of all four sets. Half the blind set's failing records are a
persona and stage no row covers, answered by the generic row and queued for review. 98 and 661
tests at 100 percent line, branch and method coverage; mutation scores of 82 and 87 percent are
required checks on `dev` (D88, D90), and warnings are errors (D89).

## One bug found and fixed

`JsonArrayRecordWriter.FlushAsync` never flushed the writer it wrapped, so a row could sit in a
buffered `StreamWriter` instead of reaching disk. The existing test used an unbuffered
`StringWriter` and could not see it; a PR #33 review found it by reading. The new test writes one
row to a real file without disposing and reads the same path through a second handle: it found
nothing on the unfixed code and the row once the flush was added.

## Open questions

Known at the tag and scheduled for Sprint 16: an empty-body completion is counted as zero tokens
(D92), and the command line's mutation score sits 0.76 points over its pin (D93). Unscheduled: a
no-completion-choice call loses its retry count; the 365-day cap on a rules-file slot has no data
behind it (A25); and a rules-file parse error reports a zero-based line. The open questions of
[DESIGN.md](DESIGN.md) section 4 stand.

## Human review, playbook step 94

The files carrying the compliance and security load that changed since `v1.0.0`, for the owner
to read by hand; not yet read:

1. `src/Agent/Decisions/RulesFileLoader.cs`: a new external input; every refusal names a row and
   carries no text from the file.
2. `src/Agent.Cli/CliRunner.cs`: the key reads near lines 792 and 825, and the rules file loading
   before any composer, record or output exists.
3. `src/Agent/Safety/DraftValidation.cs` and `src/Agent/Orchestration/LeasingMessageAgent.cs`:
   the one validation per record, and when the orchestrator accepts the wrapper's verdict.
4. `src/Agent/Safety/SafetyValidator.cs` and `src/Agent/Safety/ValidatingMessageComposer.cs`.
5. `src/Agent/Orchestration/ReviewQueueEntry.cs`: the queue row and its log line, which carries no
   record text.
6. `src/Agent/Ingest/JsonlRecordReader.cs` and `src/Agent/Ingest/JsonArrayRecordWriter.cs`: the
   streamed input and output.

## Phase 8 check

From the main checkout at `6e05adb` on 2026-09-11: `dotnet build` 0 warnings, 0 errors; the
README run command exit 0; the hold-out exit 0 at 12 of 12 with three log lines under
`TaskId=prospect_welcome_day0` in `--log-file`; `synthetic_12.jsonl` exit 2 at 12 of 13; and the
three replays exit 0, 2 and 2 with the live tallies and Safety 0/0. The fresh-clone run of all 14
runbook assertions was on 2026-09-10 at an earlier commit of the same sprint.

## Exceptions on record

D74 and D75 stand. The Sprint 16 agents started before CI on this commit finished, against the
rule that the next sprint waits for the current one to be green; nothing of theirs merged first.
