# Operations: running, debugging, and reading logs

This is the one place that answers "how do I actually run this thing, and
when it does something wrong, where do I look." [TalkingPoints.md](../TalkingPoints.md)
explains *why* the logging system looks the way it does (Sprint 8's audit,
Sprint 9's build-out); this document is the reference for *using* it,
written so it stands on its own without that history.

## 1. Running it

```bash
dotnet build                                                  # build the whole solution
.\test.ps1                                                    # run the suite; fails the build under 100% coverage
dotnet run --project src/Agent.Cli -- --input <file> --output <file> [options]
```

| Flag | Required | Purpose |
|---|---|---|
| `--input <file.jsonl>` | yes | The prospect/resident cases to process, one JSON object per line. |
| `--output <file.json>` | yes | Where the agent's decisions (`AgentOutput` per record) are written, as one indented JSON array. |
| `--composer template\|openai` | no (default `template`) | `template` is deterministic and free; `openai` calls a real completion model and needs `OpenAI:ApiKey` set via `dotnet user-secrets` (never hardcoded, never handled by an agent). |
| `--diagnostics <file.json>` | no | Per-record domain diagnostics (`consent_verified`, `fair_housing_check_passed`, `brand_style_applied`, `safety_violation_count`) — what the agent decided and why, not what the process did. Different thing from logging; see the note in section 3. |
| `--eval-report <file.txt>` | no | Scores `--output`'s results against each record's labeled `expected` field, if present. Prints to the console and writes to the given file. A record with no `expected` shows up as an unscoreable row rather than aborting the report. |
| `--log-file <file.log>` | no | Persists structured log lines to a real file. Without it, logs still go to the console (see section 3) — this only adds a second, durable sink. |

Nothing above requires all of it at once. The smallest useful run is
`--input` + `--output`; add the others as the question changes from
"what did it decide" to "did it decide correctly" to "what actually
happened while it ran."

## 2. Debugging a bad run

Start with the exit code (`CliExitCodes` in `src/Agent.Cli/CliRunner.cs`):

| Exit code | Meaning | Next step |
|---|---|---|
| `0` (Success) | Every record in `--input` was processed without an unhandled exception. | Nothing to debug — a suppressed message (`next_message: null`) is a valid *decision*, not a failure. Check `--diagnostics` if a suppression looks wrong. |
| `1` (UsageError) | Bad CLI arguments, an unknown `--composer` value, or a missing `OpenAI:ApiKey`. | Read the plain-text line on stderr — it names exactly what was wrong (composer name, or the `dotnet user-secrets set` command to run). Nothing else ran; no records were processed. |
| `2` (PartialFailure) | At least one record threw an unhandled exception during processing. | Every other record still completed and is in `--output` — this is deliberate per-record isolation, not a partial write. Find which record via the stderr line (`Record '<TaskId>' failed: <ExceptionType>: <message>`), or the log (see below) for the full stack trace. |

**Where to look, in order:**

1. **Plain stderr text**, written by `CliRunner` regardless of any logging
   configuration. This is the CLI's stable, always-on contract — usage
   errors, which record failed and why (type + message, no stack trace), and
   which eval-report rows were unscoreable. Sufficient for "what broke."
2. **The log** (console JSON, or `--log-file` if given) for "why, exactly,
   and what led up to it." Every stderr-reported failure has a matching
   `Error`-level log entry carrying the *full* exception object — type,
   message, and stack trace — not just the one-line summary stderr gets.
   Search the log for the failing record's `TaskId`; every log line emitted
   anywhere during that record's processing carries it (see section 3).
3. **`--diagnostics`**, only if the question is "why did the agent decide
   X for this record" rather than "why did the process fail." A `null`
   `fair_housing_check_passed` means the message was suppressed before
   validation ever ran (no consent, or composition failed) — not a bug.

## 3. How logging actually works

Built on `Microsoft.Extensions.Logging`. Two independent sinks, both
structured JSON, both carrying the same information:

- **Console**, always on. JSON-formatted (`ConsoleFormatterNames.Json`),
  written to stdout via the standard console log provider.
- **File**, only when `--log-file <path>` is given. A from-scratch
  `Agent.Cli.Logging.FileLoggerProvider` — plain text lines, not JSON (see
  section 4 for the exact shape), appended to the given path.

Both sinks render the same `ILogger` calls and the same log scopes; nothing
is console-only or file-only.

**This is not the same thing as `--diagnostics`.** `--diagnostics` is a
domain artifact — the agent's own record of what it decided
(`AgentDiagnostics`), part of the graded output contract. The log is process
telemetry — what the code did while producing that decision. A run can have
perfect diagnostics and a log full of retries, or a suppressed message with
a totally quiet log (no consent, nothing went wrong, there was just nothing
to do).

**Correlation:** `LeasingMessageAgent.RunAsync` and `Evaluator.Evaluate`
each open a log scope carrying `TaskId` at the start of processing one
record. Every log line emitted anywhere downstream during that record's
processing — inside `ValidatingMessageComposer`, `OpenAiMessageComposer`,
the CLI's own per-record lines — carries that `TaskId`, without any of those
classes needing to accept or pass it explicitly. This is why searching a log
for one `TaskId` gives the complete story of that one record, not a mix of
every record interleaved.

**Log levels, and what each one means here:**

| Level | Meaning in this codebase | Example |
|---|---|---|
| `Information` | A normal lifecycle event — nothing went wrong. | "Message composed", "Record processed in Nms", "Batch complete: N records, M failures" |
| `Warning` | Something didn't go as hoped, but the system already has a handled path for it — a retry, a fallback, a degraded-but-valid outcome. | A compose attempt failed safety validation and is retrying; falling back to the template composer; an eval record has no `expected` to score against |
| `Error` | Something is being lost or is genuinely unexpected — not a path the system was designed to recover from. | A record's processing threw and that record is dropped from `--output`; scoring threw and the record becomes unscoreable; the fallback composer also failed |

If you only want to know "is anything actually broken," filtering to
`Error` is the right first pass — `Warning` is the system coping, not the
system failing.

## 4. How to read one log line

**Console (JSON):**

```json
{"EventId":0,"LogLevel":"Error","Category":"Agent.Cli.CliRunner","Message":"Record 't2' failed.","Exception":"System.ArgumentOutOfRangeException: Move date target cannot precede the last interaction date. (Parameter 'moveDateTarget')\n   at Agent.Decisions.NextActionPlanner.Plan(...)\n   at ...","State":{"Message":"Record 't2' failed.","TaskId":"t2","{OriginalFormat}":"Record '{TaskId}' failed."},"Scopes":[]}
```

- `Category` — the fully-qualified class that logged this, e.g.
  `Agent.Cli.CliRunner` or `Agent.Orchestration.LeasingMessageAgent`. Tells
  you which layer this line came from.
- `Message` — the rendered text, already substituted (`{TaskId}` filled in).
- `State.{OriginalFormat}` — the unsubstituted template, plus each named
  parameter (`TaskId`, `ElapsedMs`, etc.) as its own field. This is the part
  worth grepping/filtering on programmatically — `Message` is for a human,
  `State`'s named fields are for a script.
- `Exception` — present only on `Error`/`Warning` calls that logged one.
  Full `ToString()`: type, message, and stack trace. This is the field that
  makes a log line strictly more useful than the plain stderr text, which
  only ever gets type + message.
- `Scopes` — every `BeginScope` active when this line was logged. A record
  processed inside `LeasingMessageAgent.RunAsync`'s scope will show `TaskId`
  here (nested inside the scope entry's own fields) even though this
  particular `Category` (`CliRunner`) never explicitly logged it — that's
  the correlation ID working as designed.

**File (`--log-file`):**

```
2026-09-05T00:12:42.8090022+00:00 [Information] Agent.Orchestration.LeasingMessageAgent: Message composed: channel=Sms, nextAction=start_cadence. TaskId=t1
2026-09-05T00:12:42.8098765+00:00 [Error] Agent.Cli.CliRunner: Record 't2' failed.
System.ArgumentOutOfRangeException: Move date target cannot precede the last interaction date. (Parameter 'moveDateTarget')
   at Agent.Decisions.NextActionPlanner.Plan(DateOnly moveDateTarget, DateTimeOffset lastInteraction, String timeZoneId) in ...
   at Agent.Orchestration.LeasingMessageAgent.RunAsync(...)
```

One line per log call: `{ISO-8601 UTC timestamp} [{Level}] {Category}: {Message}{scope pairs, space-separated Key=Value}`.
An exception, if present, is appended as its full `ToString()` on the
following line(s) — not truncated, not summarized.

## 5. The rules that keep this readable

These are enforced by convention (code review), not by a linter — stated
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
   message just to make that one line greppable in isolation — that both
   duplicates data already on every line via the scope and invites the two
   copies drifting (one updated, one not) if the record's identity ever
   needs to change mid-flight.
4. **Plain CLI stderr text and the structured log are two different
   channels serving two different readers — never fold one into the
   other.** stderr is the stable, human-first contract a person watches
   while the CLI runs and that `Agent.Cli.Tests` asserts against directly;
   the log is the detailed, machine-parseable record for after the fact.
   A change that removes the plain-text line because "it's all in the log
   now" breaks the first reader to save duplicating effort for the second.
5. **No log line is truncated or summarized to "keep it clean."** A long
   stack trace is exactly as long as it needs to be. "Readable" means every
   line answers its own question completely, not that every line is short.
