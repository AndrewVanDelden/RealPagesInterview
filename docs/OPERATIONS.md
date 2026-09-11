# Operations: running, debugging, and reading logs

This is the one place that answers "how do I actually run this thing, and
when it does something wrong, where do I look." PR #12 (Sprint 9, real logging) and the
Sprint 8 audit in the git history of `TalkingPoints.md`, a file deleted in D80, explain *why* the
logging system looks the way it does; this document is the reference for *using* it,
written so it stands on its own without that history.

## 1. Running it

```bash
dotnet build                                                  # build the whole solution
.\test.ps1                                                    # run the suite; fails the build under 100% coverage; exits with dotnet test's exit code
dotnet run --project src/Agent.Cli -- --input <file> --output <file> [--now <ISO-8601>] [options]
dotnet run --project src/Agent.Cli -- --input holdout_12.jsonl --output out.json --now 2025-12-09T00:00:00-06:00 --eval-report eval.txt --diagnostics diag.json
dotnet run --project src/Agent.Cli -- --input synthetic_12.jsonl --output out.json --now 2026-03-07T12:00:00Z --eval-report eval.txt
dotnet run --project src/Agent.Cli -- --input holdout_12.jsonl --replay out.json --eval-report eval.txt
dotnet run --project src/Agent.Cli -- --input holdout_12.jsonl --replay out.json --eval-report eval.txt --judge
```

| Flag | Required | Purpose |
|---|---|---|
| `--input <file.jsonl>` | yes | The prospect/resident cases to process, one JSON object per line. |
| `--output <file.json>` | yes, unless `--replay` | Where the agent's decisions (`AgentOutput` per record) are written, as one indented JSON array. Rows are written as records finish, in input order, so a cancelled run leaves the file holding the rows written so far with no closing bracket, which does not parse as a finished array. |
| `--replay <file.json>` | no | Re-score an existing `--output` file against `--input` without running the agent (D14). Rows pair with the parsed records by position; a file that is not a JSON array, or whose row count differs from the parsed input, is refused with exit code 1. Safety and latency read `n/a` in replay: they exist only in the run that wrote the file. |
| `--now <ISO-8601 date-time>` | no (default: the current UTC time) | The run's reference time (D10): the day send times are floored to and horizons are counted from. The documented run against `holdout_12.jsonl` passes `2025-12-09T00:00:00-06:00`, the oracle's date. Logged once per run. |
| `--composer template\|openai` | no (default `template`) | `template` is deterministic and free; `openai` calls a real completion model and needs `OpenAI:ApiKey` set via `dotnet user-secrets` (never hardcoded, never handled by an agent). |
| `--model-call-budget-ms <n>` | no (default: the strictest `p95_latency_ms` the batch states) | With `--composer openai` only: what one attempt of a model call may take, in place of the budget D28 derives from the records (D70). A timeout is never retried (D33); a call retried after a transient status gets the whole budget again, so it can take up to twice the budget plus the SDK's backoff (D35). A positive whole number of milliseconds; anything else, or the flag on any other composer, exits 1. It exists for evaluation runs: on the three sets here the records state 2000 ms, and a completion measured about 1.5 to 4.5 seconds on 2026-09-10, so without the flag the model answers only the records whose completion happens to fit, 2 of 10 on the synthetic set that day, and the template writes the rest. The scorecard's p95 check still reads the records' own budget, so a run under the flag reports its p95 against 2000 ms and fails it, which is the true cost of the model rather than a hidden one. |
| `--diagnostics <file.json>` | no | Per-record domain diagnostics: `diagnostics` (`required_states`, `brand_style_failures`, `safety_violation_count`, `suppression_reason`, `action_plan`, `schedule`, `composition`, `model_cost`, `network_retries`), `ingest_notes` (`defaulted_fields`, `unknown_members`) and `latency_ms`, what the agent decided and why and what the record did not carry, not what the process did. `latency_ms` is a number, the wall-clock milliseconds of exactly one agent run for that record and the same value the scorecard's p95 is computed from (D61). It excludes reading and parsing the input line, every output write and the evaluator, and it includes the channel decision, the plan, every compose attempt with any model call inside it, the schedule and the safety gate. Being wall clock, it moves between runs on the same code, so no test pins it. `model_cost` is what that record's model calls cost, in tokens and never in money (D62). It sits on the `diagnostics` object itself, beside `composition` rather than inside it, and so does `network_retries` (D66): both are facts about what this record's run spent rather than properties of a returned message, so a record whose `composition` is null still carries them. `model_cost` is therefore `null` only when no model call was made at all, never because the record was suppressed, refused or failed in composition; a record that called the model twice and ended with no message reports those two calls. Otherwise it is `{calls, completed_calls, input_tokens, output_tokens}`. A call abandoned at its timeout is counted in `calls` with `completed_calls` and both token counts zero, because the client throws before it can read the vendor's usage block; a completed call carries the vendor's own counts. Different thing from logging; see the note in section 3. |
| `--eval-report <file.txt>` | no | Scores `--output`'s results against each record's labeled `expected` field, if present. Prints to the console and writes to the given file. One row per record with `OK`, `FAIL`, or `n/a` (not measured: no threshold stated, no message to check, or no value recorded) per check: channel, send day, send hour, action type, opt-out, call-to-action type, call-to-action payload, language, safety, personalization (with its coverage score), and the judge's `ActionSem` and `BodySem`, which read `n/a` unless the run passed `--judge`. Then a `Checks:` line of passed over measured per check, the batch p95 latency against the strictest stated budget, a `Batch latency:` line, which is one wall-clock measurement around the whole record loop, reading and parsing each input line and writing each record's rows included, rather than a percentile of the rows (D61), a `Batch model cost:` line, which is the run's token totals in the form `N call(s), N completed, N input + N output token(s)` and never a money figure (D62), and the overall count. The two batch lines read `n/a` and `none` on a `--replay` run, which re-scores a file and so measures no batch and makes no model call. A record with no `expected` shows up as an unscoreable row rather than aborting the report. So does every input the batch could not process (D71): a line that did not parse is a row reading `(did not parse)` with `ERROR:` and the reader's failure text, which names the line number and never its content, and a record that threw is a row under its own task id with `ERROR: Record failed:` and the exception type. Each counts in the overall and never passes, and measures no check, so the `Checks:` line does not move; `synthetic_12.jsonl`'s malformed line 11 is why that set reads `Overall: 12/13 passed`. |
| `--judge` | no (default: off) | Adds the two semantic checks `ActionSem` and `BodySem` to `--eval-report`'s scorecard (D30). A pinned model, `gpt-4o`, grades under a pinned rubric whether the produced message conveys the label's own action and body; it is reference-based, so it grades against the label the customer wrote, never against its own taste. A presence flag, not an option with a value: there is one judge, and its model is pinned in code rather than configured, because a grade only means something next to yesterday's grade if the same model and the same rubric produced both (playbook step 31). It needs `OpenAI:ApiKey` set via `dotnet user-secrets` and makes one model call per scoreable record, so it costs one call per record every time it is asked for. Its two verdicts are their own checks and are excluded from a record's pass or fail, so the judge can never overturn a deterministic check, and a run with the network down reports both as `n/a`. Applies to a normal run and to `--replay` alike. |
| `--review-queue <path>` | no | The work list of what the safety gate rejected (D43): one row per record the safety validator suppressed, carrying the task id, every violation by check, and the rejected draft, which is where the channel is read from. Empty on a healthy run, which is the point of it; its length is the number to report. A record the channel selector returned no value for is not in it, because no consented channel is the correct decision rather than a failure, and composition failure is not in it either, because there is no draft to review. This is the one place the program deliberately writes prospect text to a file: the queue carries the rejected draft by design, since it is a file a reviewer opens rather than a log line, and the redaction rule that governs logs does not govern it. |
| `--log-file <file.log>` | no | Persists structured log lines to a real file. Without it, logs still go to the console's stderr stream (see section 3), this only adds a second, durable sink. |

Nothing above requires all of it at once. The smallest useful run is
`--input` + `--output`; add the others as the question changes from
"what did it decide" to "did it decide correctly" to "what actually
happened while it ran."

## 2. Debugging a bad run

Start with the exit code (`CliExitCodes` in `src/Agent.Cli/CliRunner.cs`):

| Exit code | Meaning | Next step |
|---|---|---|
| `0` (Success) | Every record in `--input` was processed without an unhandled exception. | Nothing to debug: a suppressed message (a `next_message` with channel `none`) is a valid *decision*, not a failure. `--diagnostics` names the `suppression_reason`. |
| `1` (UsageError) | Bad CLI arguments, an unknown `--composer` value, a `--model-call-budget-ms` that is not a positive whole number or is given without `--composer openai` (D70), a missing `OpenAI:ApiKey` for `--composer openai` or for `--judge` (D30), a `--replay` file that is not a JSON array or whose row count differs from the parsed input, a path that cannot be opened for writing on any of the four output flags, `--output`, `--diagnostics`, `--review-queue` and `--eval-report`, as it already was on `--log-file` (D64), a path that cannot be opened for reading on `--input` or `--replay` (D65), or any argument given an empty value, since no argument of this program may be empty (D65). | Read the plain-text line on stderr, it names exactly what was wrong (composer name, the `dotnet user-secrets set` command to run, both counts, or the flag whose path could not be opened, `Could not open --output '<path>': ...` and `Could not open --input '<path>': ...` alike). An empty value is reported in one of two forms, by the flag it followed or by its position when it followed nothing: `The argument after '--input' is empty: no argument of this program may be empty.` and `Argument 1 is empty: no argument of this program may be empty.` Usually nothing else ran: the input opens and the three batch streams open before the record loop, so a bad path costs no record and no model call. `--eval-report` is the exception, because its report describes a batch that has to have happened first: a failed write there turns a run that would have exited 0 or 2 into exit 1, with `--output` already complete on disk. |
| `2` (PartialFailure) | At least one line of `--input` did not parse, or at least one record threw an unhandled exception during processing. With `--eval-report`, each is also an `ERROR` row of the scorecard, counted in the overall (D71). | Every other record still completed and is in `--output`, this is deliberate per-record isolation, not a partial write. Find which record via the stderr line (`Record '<TaskId>' failed: <ExceptionType>: <message>`), or the log (see below) for the full stack trace. |

**Where to look, in order:**

1. **Plain stderr text**, written by `CliRunner` regardless of any logging
   configuration. This is the CLI's stable, always-on contract, usage
   errors, which record failed and why (type + message, no stack trace), and
   which eval-report rows were unscoreable. Sufficient for "what broke."
2. **The log** (console, interleaved with the plain stderr text above, or
   `--log-file` if given) for "why, exactly, and what led up to it." Every
   stderr-reported failure has a matching `Error`-level log entry carrying
   the *full* exception object, type, message, and stack trace, not just
   the one-line summary stderr gets. Search the log for the failing
   record's `TaskId`; every log line emitted anywhere during that record's
   processing carries it (see section 3). Since D37 the batch runs up to
   four records at once, so their log lines interleave in time; each stderr
   failure line, `Record '<TaskId>' failed` and `Record failed to parse`
   alike, is written as soon as every earlier record has finished, still in
   input order.
3. **`--diagnostics`**, only if the question is "why did the agent decide
   X for this record" rather than "why did the process fail." `required_states`
   answers every name the record's own `assertions.required_states` listed,
   one of `earned`, `not_earned`, `not_evaluated` (nothing was checked,
   because the record was suppressed before the check ran) and
   `no_check_defined` (a state name this program has no check for, which is
   recorded by name rather than claimed: D42, A14). `brand_style_failures`
   names which of the three brand rules broke, so a `not_earned`
   `brand_style_applied` does not have to be guessed at.
   `action_plan` says how `next_action` was reached: `branch` (`short` or
   `long`), `horizon_days` (null when the record states no move date, which
   is unstated rather than zero), and `source`, which is `catalog_row` when
   a row for that persona and lifecycle stage stated the action,
   `generic_row_no_branch` when a row matched but had no evidence for that
   horizon branch, and `generic_row_no_match` when no row exists for that
   persona and stage. A whole `action_plan` of `null` means the channel
   selector returned no value for the record, so the planner never ran.
   `schedule` says how `send_at` was reached: `floor`, which is
   `last_interaction` when the record's last interaction is after the run's
   reference time and `reference_time` otherwise, `time_zone_id`, the zone
   the slot was resolved in and so `UTC` on a record whose timezone the
   runtime does not know, and `slot`, which is `exact` on every record the
   current zone database can produce, `shifted_past_gap` when the zone
   sprang forward across the slot, and `earlier_of_two` when it fell back
   across it. A whole `schedule` of `null` means the scheduler never ran:
   the channel selector returned no value, or composition failed before it.
   `composition` says how the message was written (D24): `composer`, the
   implementation whose text was returned, `template` or `openai`, the same
   two spellings `--composer` takes, so a record reading `template` on an
   `openai` run is one the fallback answered, which is where a model call
   that ran past the batch's stated budget shows up (D28); `attempts`, how
   many compose calls the compose-validate loop made to get that text; and
   `locale_applied`, whether the composer could serve the record's stated
   language, which is a check that can fail on the template composer, since
   it holds one template set per language it serves, English and Spanish
   today, and is true on the model composer, which passes the tag through
   with no allowlist anywhere (D26, A13). All three are properties of a
   returned message, so a whole `composition` of `null` means the record
   carries no message, which is no consented channel or a composition
   failure, and `suppression_reason` separates those two. The two spend
   counts sit outside that object, on the `diagnostics` object itself,
   precisely so a null `composition` does not take them with it (D66):
   `network_retries` is how many transport retries the calls underneath
   spent, which since D33 are retries after a transient status (408, 429,
   500, 502, 503, 504) and never after a timeout or a request that got no
   response, `null` for a run that made no network call at all (D28), and
   `model_cost` is the token counts described in section 1. A record
   refused at the safety gate or failed in composition made its model calls
   and was billed for them, and these are the two members that still say so.
4. **`--review-queue`**, when the question is not about the process or about
   one record's reasoning at all, but "what did the safety gate reject, and
   does a person need to read it." That is a different question from the
   three above and it wants a different artifact (D43). `--diagnostics` is a
   full per-record dump of how every decision was reached, written for every
   record, and you open it knowing which record you care about; the review
   queue is a work list, it holds only the records the safety validator
   suppressed, and on a healthy run it is empty. So: reach for the queue to
   find out *whether* there is anything to look at and to read the rejected
   draft itself, and for `--diagnostics` to work out why a record you have
   already identified decided what it did. Two suppressions are deliberately
   absent from the queue and are not bugs: a record with no consented channel
   never reaches the safety gate, and a record whose composition failed has no
   draft to review. `suppression_reason` in `--diagnostics` still names which
   of the three happened.

## 3. How logging actually works

Built on `Microsoft.Extensions.Logging`. Two independent sinks, both plain
text, both rendering the exact same format (section 4), both carrying the
same information:

- **Console**, always on, via `Agent.Cli.Logging.ConsoleLoggerProvider`,
  written through the CLI's own injected `error` stream (`Console.Error` in
  production), not a raw stdout console provider. Two reasons for that:
  `--eval-report`'s scorecard text goes to stdout, and a console provider
  hardwired to the real `Console.Out`/`Console.Error` would collide with it;
  and `CliRunnerTests` injects its own `TextWriter` for `output`/`error`
  specifically to avoid touching the real console, which a hardwired
  provider would defeat.
- **File**, only when `--log-file <path>` is given. A from-scratch
  `Agent.Cli.Logging.FileLoggerProvider`, appended to the given path -
  functionally the same sink as Console, just durable across runs and
  written to a path instead of a stream.

Both sinks share their line-formatting code (`Agent.Cli.Logging.LogLineFormatter`)
and render the same `ILogger` calls and the same log scopes; nothing is
console-only or file-only.

**This is not the same thing as `--diagnostics`.** `--diagnostics` is a
domain artifact, the agent's own record of what it decided
(`AgentDiagnostics`), part of the graded output contract. The log is process
telemetry, what the code did while producing that decision. A run can have
perfect diagnostics and a log full of retries, or a suppressed message with
a totally quiet log (no consent, nothing went wrong, there was just nothing
to do).

**Correlation:** `CliRunner`'s per-record loop is the one owner of the `TaskId`
log scope (D16). Every log line emitted anywhere downstream during that
record's processing, inside `LeasingMessageAgent`, `ValidatingMessageComposer`,
`OpenAiMessageComposer`, the CLI's own per-record lines, carries that
`TaskId` via the scope, without any of those classes needing to accept or
pass it explicitly, and without restating it in their own message text
(rule 3, section 5). Neither the agent nor the evaluator opens a scope of its
own: two scopes pushing the same key rendered every line as `TaskId=x TaskId=x`.
The evaluator runs after the loop, so its one failure line names the task id
in the message instead. A library caller that wants correlation opens its own
scope the way the CLI does. This is why searching a log for one `TaskId` gives
the complete story of that one record, not a mix of every record interleaved,
which matters since D37: up to four records run at once, so the raw log does
interleave them, and the `TaskId` on each line is what separates them.

**Log levels, and what each one means here:**

| Level | Meaning in this codebase | Example |
|---|---|---|
| `Information` | A normal lifecycle event, nothing went wrong. | "Message composed", "Next action came from the generic row", "Record processed in Nms", "Batch complete: N records, M failures" |
| `Warning` | Something didn't go as hoped, but the system already has a handled path for it, a retry, a fallback, a degraded-but-valid outcome. | A compose attempt failed safety validation and is retrying; falling back to the template composer; an eval record has no `expected` to score against |
| `Error` | Something is being lost or is genuinely unexpected, not a path the system was designed to recover from. | A record's processing threw and that record is dropped from `--output`; scoring threw and the record becomes unscoreable; the fallback composer also failed |

If you only want to know "is anything actually broken," filtering to
`Error` is the right first pass, `Warning` is the system coping, not the
system failing.

## 4. How to read one log line

Both sinks (console via stderr, and `--log-file` if given) render the exact
same format, since both go through `Agent.Cli.Logging.LogLineFormatter`:

```
2026-09-05T00:12:42.8090022+00:00 [Information] Agent.Orchestration.LeasingMessageAgent: Message composed: channel=Sms, nextAction=start_cadence. TaskId=t1
2026-09-05T00:12:42.8098765+00:00 [Error] Agent.Cli.CliRunner: Record failed. TaskId=t2
System.ArgumentOutOfRangeException: Move date target cannot precede the last interaction date. (Parameter 'moveDateTarget')
   at Agent.Decisions.NextActionPlanner.Plan(DateOnly moveDateTarget, DateTimeOffset lastInteraction, String timeZoneId) in ...
   at Agent.Orchestration.LeasingMessageAgent.RunAsync(...)
```

One line per log call: `{ISO-8601 UTC timestamp} [{Level}] {Category}: {Message}{scope pairs, space-separated Key=Value}`.

- `{Category}`, the fully-qualified class that logged this, e.g.
  `Agent.Cli.CliRunner` or `Agent.Orchestration.LeasingMessageAgent`. Tells
  you which layer this line came from.
- `{Message}`, the rendered text. Never restates `TaskId` (rule 3) - that
  comes from the scope suffix instead.
- `TaskId=...`, the active `BeginScope` value, appended after the message
  (rule 3, section 5). Both lines above carry it even though `CliRunner`'s
  own line never explicitly logged it - that's the correlation ID working
  as designed, the same way it does for every other line in the system.
- An exception, if present, is appended as its full `ToString()` on the
  following line(s) - not truncated, not summarized - which is what makes a
  log line strictly more useful than the plain stderr text, which only ever
  gets type + message.

## 5. The rules that keep this readable

These are enforced by convention (code review), not by a linter, stated
here so a change that violates one gets caught on sight:

1. **`Warning` means handled; `Error` means something was lost.** Never log
   `Error` for a retry that's about to succeed, and never log `Warning` for
   an exception that's about to make a whole record disappear from
   `--output`. The level is the first (and fastest) thing anyone reads.
2. **Always pass the exception object to the logger
   (`log.LogError(ex, "message")`), never just interpolate `ex.Message`
   into the text.** The logger call is the one place in this codebase where
   the full stack trace survives; a hand-built string throws it away for no
   benefit.
3. **The correlation ID is a scope, never a repeated parameter.** `TaskId`
   is attached once, via `BeginScope`, at the top of processing one record.
   No individual log call downstream re-states it as `{TaskId}` in its own
   message just to make that one line greppable in isolation, that both
   duplicates data already on every line via the scope and invites the two
   copies drifting (one updated, one not) if the record's identity ever
   needs to change mid-flight.
4. **Plain CLI stderr text and the structured log are two different
   contracts serving two different readers, never fold one into the
   other, even though both happen to write to the same stream by default.**
   stderr's plain-text lines are the stable, human-first contract a person
   watches while the CLI runs and that `Agent.Cli.Tests` asserts against
   directly; the log is the detailed, timestamped, scope-correlated record
   for after the fact - a different shape, not just a different stream.
   A change that removes the plain-text line because "it's all in the log
   now" breaks the first reader to save duplicating effort for the second.
5. **No log line is truncated or summarized to "keep it clean."** A long
   stack trace is exactly as long as it needs to be. "Readable" means every
   line answers its own question completely, not that every line is short.
