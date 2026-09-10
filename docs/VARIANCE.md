# Variance report (playbook step 83)

Three runs of `synthetic_12.jsonl` through the full entry point with the real model on
2026-09-10, same code, same command, only the run number changed:

```bash
dotnet run --project src/Agent.Cli -- --input synthetic_12.jsonl --output out.json --now 2026-03-07T12:00:00Z --composer openai --model-call-budget-ms 30000 --eval-report docs/scorecards/synthetic_12_openai_run1.txt
```

The model is `gpt-4o-mini` at temperature 0.2 with no seed (`OpenAiCompletionClient`), and
`--model-call-budget-ms` is D70's flag, without which the model answers nothing on this set
(D31). The scorecards are the CLI's own output, unedited:
[run 1](scorecards/synthetic_12_openai_run1.txt), [run 2](scorecards/synthetic_12_openai_run2.txt),
[run 3](scorecards/synthetic_12_openai_run3.txt), and the offline baseline
[template](scorecards/synthetic_12_template.txt). The per-record columns below come from each
run's `--diagnostics` and `--output` files, which were read and not committed.

## Batch numbers

| Run | Overall | CTA | Every other check | p95 | Batch latency | Calls | Input tokens | Output tokens | Model wrote | Template fallback | Drafts rejected |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 11/13 | 9/10 | unmoved | 8,043 ms | 43,978 ms | 19 | 7,479 | 2,129 | 8 | 2 | 11 |
| 2 | 11/13 | 9/10 | unmoved | 8,261 ms | 54,266 ms | 19 | 7,479 | 1,996 | 9 | 1 | 10 |
| 3 | 12/13 | 10/10 | unmoved | 9,356 ms | 50,107 ms | 19 | 7,479 | 2,183 | 7 | 3 | 12 |
| template | 12/13 | 10/10 | unmoved | 19 ms | 30 ms | 0 | 0 | 0 | 0 | 10 | 0 |

"Unmoved" is channel 12/12, day 10/10, hour 10/10, action 12/12, opt-out 10/10, payload 10/10,
language 10/10, safety 12/12 and personalization 9/9 on every run. The thirteenth row is line 11,
malformed by design (D71). Every p95 fails the records' own 2000 ms. Every rejected draft was
rejected for one reason, `Missing required opt-out instructions`.

## Per record

Each cell is composer, attempts, latency in ms, and the call-to-action type sent. The last column
counts the distinct message bodies across the three runs.

| Record | Run 1 | Run 2 | Run 3 | Distinct bodies |
|---|---|---|---|---|
| `synthetic_01` (suppressed) | none | none | none | 0 |
| `synthetic_02` | template, 3, 4595, schedule_tour | openai, 2, 5070, schedule_tour | openai, 2, 6623, schedule_tour | 3 |
| `synthetic_03` | openai, 2, 3384, schedule_tour | openai, 2, 5876, schedule_tour | openai, 2, 9356, schedule_tour | 2 |
| `synthetic_04` (Spanish) | template, 3, 4250, schedule_tour | template, 3, 6043, schedule_tour | template, 3, 4322, schedule_tour | 1 |
| `synthetic_05` | openai, 2, 4576, contact | openai, 2, 4178, contact | template, 3, 3829, reply | 3 |
| `synthetic_06` | openai, 2, 4085, schedule_tour | openai, 2, 5199, schedule_tour | openai, 2, 4976, schedule_tour | 3 |
| `synthetic_07` (suppressed) | none | none | none | 0 |
| `synthetic_08` | openai, 2, 4493, schedule_tour | openai, 1, 2947, schedule_tour | openai, 2, 3864, schedule_tour | 3 |
| `synthetic_09` | openai, 2, 4360, schedule_tour | openai, 2, 8261, schedule_tour | template, 3, 5030, schedule_tour | 3 |
| `synthetic_10` | openai, 2, 4289, schedule_tour | openai, 2, 8162, schedule_tour | openai, 2, 6159, schedule_tour | 3 |
| `synthetic_12` | openai, 2, 8043, call_leasing_office | openai, 2, 4286, call_leasing_office | openai, 2, 4267, call_leasing_office | 3 |
| `synthetic_13` | openai, 1, 1895, schedule_tour | openai, 2, 4235, schedule_tour | openai, 1, 1671, schedule_tour | 3 |

## What varies and what does not

- **The decisions do not vary.** Channel, send time, action type and the call-to-action payload
  are identical on all three runs and equal to the template's, because code owns them and the
  model writes prose (S2). Opt-out, language and safety pass on every message that shipped,
  because the safety gate refuses any draft that fails them.
- **The prose varies on every run.** Every record the model wrote more than once has a different
  body each time except `synthetic_03`, which repeated once. Temperature 0.2 does not make it
  reproducible, which is what step 52 says to expect.
- **Whether the fallback fires varies**, and that is what moves the score. The model wrote 8, 9
  and 7 of the 10 messages. The one check that moved, CTA at 9, 9 and 10, moved on
  `synthetic_05`: that record states no `primary_cta` and its label expects `reply`; the model
  chose `contact` on runs 1 and 2 and failed, and on run 3 both drafts were rejected, the
  template wrote `reply`, and it passed. The model never got that record right.
- **The Spanish record never shipped a model draft.** `synthetic_04`'s drafts were rejected on
  both attempts on all three runs, and the template wrote all three messages.
- **Cost is stable; time is not.** 19 calls and 7,479 input tokens on every run, since the
  prompts are the same; output tokens within 10 percent of each other; per-record latency from
  1.7 to 9.4 seconds.

## What it means for this set

The model path scores 11 or 12 of 13 against the template's 12 of 13. It never beats the
template on a scored check here, it misses the stated latency threshold by roughly four times on
every run, and about a third of its drafts are refused for leaving out the opt-out instructions.
What it adds is prose that differs per record and per run, which no deterministic check on this
set rewards. The two findings behind those refusals and the `synthetic_05` miss are D72 and D73
in [DECISIONS_ARCHIVE.md](DECISIONS_ARCHIVE.md), fixed the same day (below).

## After D72 and D73 (2026-09-10)

Code now appends the opt-out sentence when a record requires one and the model left it out
(D72), and sets A9's generic call to action when a record states none (D73). The same command,
three more runs: [run 1](scorecards/synthetic_12_openai_after_d72_run1.txt),
[run 2](scorecards/synthetic_12_openai_after_d72_run2.txt),
[run 3](scorecards/synthetic_12_openai_after_d72_run3.txt).

| Run | Overall | CTA | Every other check | p95 | Batch latency | Calls | Input tokens | Output tokens | Model wrote | Template fallback | Drafts rejected |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 12/13 | 10/10 | unmoved | 2,318 ms | 18,199 ms | 10 | 3,900 | 976 | 10 | 0 | 0 |
| 2 | 12/13 | 10/10 | unmoved | 2,398 ms | 19,449 ms | 10 | 3,900 | 990 | 10 | 0 | 0 |
| 3 | 12/13 | 10/10 | unmoved | 4,508 ms | 33,399 ms | 10 | 3,900 | 975 | 10 | 0 | 0 |

- **No draft was refused on any run**, so every record makes one call: 10 calls a run where
  there were 19, 2,941 output tokens over three runs where there were 6,308, and 1.5 to 4.5
  seconds a record where it was 1.7 to 9.4.
- **`synthetic_04`, the Spanish record, is written by the model on every run**, with the Spanish
  set's `Responde STOP para cancelar.` appended.
- **`synthetic_05` carries `reply` on every run and passes**, so the score no longer depends on
  whether the fallback fires.
- **The prose still varies:** every model-written record has three distinct bodies across the
  runs except `synthetic_03` and `synthetic_04`, which have two each.
- **Brand style**, a diagnostic and not a gate (D39), reported 4 findings a run.
- **p95 still fails** the records' 2000 ms on every run.

The model path now scores the template's 12 of 13 on every run, with the scored outcome no longer
moving between runs, at about half the calls and tokens it spent before. It still does not beat
the template on a scored check here, and it still misses the stated latency threshold.

## Without the flag, after D33, D35 and D37 (2026-09-10)

One run of the documented command with `--composer openai` and no `--model-call-budget-ms`, so
each call is bounded by the records' own 2000 ms: [scorecard](scorecards/synthetic_12_openai_no_flag_after_d35.txt).
A timeout is no longer retried (D33), one attempt gets the whole 2000 ms rather than half of it
(D35), and four records run at once (D37). The model wrote 2 of the 10 messages, where the step
60 run's model wrote none of 23 when each attempt had 1000 ms; the template wrote the other 8,
whose 8 calls were abandoned at the timeout and are counted with zero tokens. 10 calls, 2
completed, 792 input and 186 output tokens, no draft refused, 12 of 13 with every check at the
template's level, p95 2,066 ms against 2000 ms and so FAIL, batch latency 6,072 ms, where the
step 60 run's sequential batch took 63 seconds for the same file.
