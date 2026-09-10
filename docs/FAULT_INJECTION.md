# Fault injection (playbook step 84)

Six faults, one section each: how the fault is injected, what the product does, what the
diagnostics say, the exit code where one applies, and the tests that prove it by file and test
name. The audit behind this file is D64 in
[DECISIONS_ARCHIVE.md](DECISIONS_ARCHIVE.md), which found five of the six already proved by the
suite and a real gap behind the sixth. Every test named below was checked against the suite on
2026-09-09 and exists.

Two things this file is not. It is not a tally: what a documented run measures is in
[DESIGN.md](DESIGN.md) section 9, and what a fault does is here. And it is not written at the
repo root, because `.gitignore` ignores the run artifacts there, so a file a run writes at the
root is not a file in the repo, which is what Phase 7's check asks for.

## 1. Network down

**Injected by.** At the composer, `FakeCompletionClient` constructed to throw an
`HttpRequestException`, which is the shape a transport failure arrives in. At whole-set scale,
outbound HTTPS blocked at the process level for the Phase 4 check run of 2026-09-08, recorded
in [DESIGN.md](DESIGN.md) section 9.

**What the product does.** `OpenAiMessageComposer` catches the transport exception and returns
`ComposeOutcome.Failed` naming the exception category and not its text. The compose-validate
loop retries to its bound of two model attempts and then composes with the template composer,
which makes no network call at all. Every record keeps its output row.

**What the diagnostics say.** `composition.composer` reads `template` on a run that asked for
`openai`, which is the fallback naming itself; `attempts` counts the model attempts and the
fallback, so 3 on the live run of 2026-09-08. `model_cost` is D62's second state:
`calls` counted, `completed_calls` and both token counts zero, because the client threw before
it could read a usage block.

**Exit code.** 0. Degradation is a valid outcome, not a failure.

**Proved by.**
`tests/Agent.Tests/Composition/OpenAiMessageComposerTests.cs`,
`ComposeAsync_CompletionClientThrowsHttpRequestException_ReturnsFailureNotException`;
`tests/Agent.Tests/Safety/ValidatingMessageComposerTests.cs`,
`ComposeAsync_ComposerKeepsFailing_FallsBackToSafeComposer` and
`ComposeAsync_BothAttemptsBad_ReportsTheFallbackComposerAndEveryAttempt`.

## 2. Rate limit

**Injected by.** `FakeHttpMessageHandler` at the transport, returning HTTP 429 with the
vendor's own error body and then a 200; and, for the exhaustion path, a handler that returns a
transient status on every attempt.

**What the product does.** The SDK's retry policy, bounded to one retry by
`OpenAiCompletionClient` (D28), retries and returns the completion. A transient status on every
attempt exhausts that bound at two attempts and the failure reaches the composer, which turns
it into a `ComposeOutcome.Failed`; from there the loop falls back exactly as in fault 1.

**What the diagnostics say.** `network_retries` reads 1 on a call that was retried
and then succeeded. That number is the transport's retries inside one call and is never counted
a second time as a model call in `model_cost.calls` (D62).

**Exit code.** 0.

**Proved by.** `tests/Agent.Tests/Composition/OpenAiCompletionClientTests.cs`,
`CompleteAsync_TransientFailureThenSuccess_RetriesOnceAndReportsIt` (one retry reported, two
HTTP calls) and `CompleteAsync_TransientFailureEveryTime_StopsAfterTheBoundedRetry` (two
attempts, then stop). The SDK's retry policy is status-agnostic across the transient set it
knows, so the exhaustion path is proved once, on a 503 rather than a 429, and not per status.

## 3. Malformed model response

**Injected by.** A fake completion client returning content that is not JSON, JSON missing the
required `body` and `cta_type`, a null JSON body, and a call-to-action type the record did not
ask for. At the client, a 200 carrying no message content and a 200 carrying no choice at all.

**What the product does.** Every one of them becomes a failure rather than an exception, so the
compose-validate loop retries and then falls back to the template composer. The 200 with no
choice is named as an `InvalidOperationException` inside the client instead of escaping as the
SDK's own `ArgumentOutOfRangeException`, which is not in the composer's catch list and would
cost that record its output row.

**What the diagnostics say.** The fallback reads as in fault 1. `model_cost` here
is D62's third state: `calls` and `completed_calls` both counted, with the vendor's real input
and output token counts, because the call completed and only what came back was unusable.

**Exit code.** 0.

**Proved by.** `tests/Agent.Tests/Composition/OpenAiMessageComposerTests.cs`,
`ComposeAsync_MalformedJson_ReturnsFailureNotException`,
`ComposeAsync_MissingRequiredFields_ReturnsFailure`, `ComposeAsync_NullJsonBody_ReturnsFailure`
and `ComposeAsync_ModelReturnsWrongCtaType_ReturnsFailure`;
`tests/Agent.Tests/Composition/OpenAiCompletionClientTests.cs`,
`CompleteAsync_ResponseHasNoContent_ThrowsInvalidOperationException` and
`CompleteAsync_ResponseHasNoChoice_ThrowsInvalidOperationException`.

## 4. Unknown locale

This product has three forms of it, and all three are proved.

**Injected by.** (a) A record stating a language tag the composer holds no template set for.
(b) A record whose timezone is not an id the runtime knows; `synthetic_12.jsonl` carries one as
record 8, so every documented synthetic run injects this fault for real. (c) A record whose
`channel_preferences` name a channel this program has no case for, including entries that are
not channel names at all and a numeric string matching a real enum ordinal.

**What the product does.** (a) Composes in English and says the locale was not applied (A13).
(b) Resolves the slot in UTC (A6). (c) Parses the name as `Unknown`, which is never opted in,
so the record takes a known consented preference or is suppressed for want of one.

**What the diagnostics say.** (a) `composition.locale_applied: false`. (b)
`schedule.time_zone_id: UTC`, and `ingest_notes` names the timezone as defaulted without
repeating the record's own unrecognized value. (c) `suppression_reason: no_contact_consent`
when no other preference was consented, which is the correct decision rather than a failure.

**Exit code.** 0.

**Proved by.** `tests/Agent.Tests/Composition/TemplateMessageComposerTests.cs`,
`ComposeAsync_LanguageWithNoTemplateSet_ComposesInEnglishAndReportsTheLocaleNotApplied`;
`tests/Agent.Tests/Decisions/SendSchedulerTests.cs`, `Resolve_UnknownTimeZoneId_ResolvesInUtc`;
`tests/Agent.Tests/Common/TimeZonesTests.cs`, `ResolveOrUtc_UnknownId_ReturnsUtc` and
`ToLocalDate_UnknownZone_UsesTheUtcDate`; `tests/Agent.Tests/Ingest/IngestNotesTests.cs`,
`Describe_UnrecognizedTimezone_NamesItAsDefaultedWithoutTheRecordsOwnValue`;
`tests/Agent.Tests/Ingest/JsonlRecordReaderTests.cs`,
`ReadAll_UnrecognizedChannelName_ParsesAsUnknownChannel`,
`ReadAll_ChannelPreferenceEntriesNotChannelNames_ParseAsUnknown` and
`ReadAll_ChannelPreferenceNumericStringMatchingARealOrdinal_ParsesAsUnknown`.

## 5. Corrupt input line

**Injected by.** `synthetic_12.jsonl` line 11, malformed by design, which every documented
synthetic run reads. At the reader, a line that is malformed JSON, one that deserializes to
null, one that is valid JSON but not an object, and one whose undeclared member is malformed.

**What the product does.** `JsonlRecordReader.ReadAll` returns one `Result` per non-blank line,
so the bad line becomes a failure row naming its line number and every other record still runs
and is still written to `--output`. Blank lines before the bad one count toward its number.

**What the diagnostics say.** Nothing, and that is correct: the record never parsed, so it has
no diagnostics row. The fact is one stderr line and one matching log entry, and the failure
names the line rather than any text the record wrote:

```
Record failed to parse: Line 11 failed to parse: JsonException: line 0, byte position 96
```

**Exit code.** 2, partial failure, which is what the documented `synthetic_12.jsonl` run exits.

**Proved by.** `tests/Agent.Tests/Ingest/JsonlRecordReaderTests.cs`,
`ReadAll_ReturnsFailureWithLineNumber_WhenLineIsMalformedJson`,
`ReadAll_ReturnsFailureWithLineNumber_WhenLineDeserializesToNull`,
`ReadAll_ReturnsFailureWithLineNumber_WhenLineIsValidJsonButNotAnObject`,
`ReadAll_OneBadLineAmongGoodOnes_ReturnsEveryOtherRecord`,
`ReadAll_BlankLinesBeforeFailingLine_CountTowardTheLineNumber`,
`ReadAll_MalformedUndeclaredMember_FailureNamesTheLineAndNoTextTheRecordWrote` and
`ReadAll_ParsesSyntheticTwelve_TwelveSuccessRowsAndOneFailureNamingLineEleven`;
`tests/Agent.Cli.Tests/CliRunnerTests.cs`,
`RunAsync_OneLineFailsToParse_OtherRecordStillWrittenAndReturnsPartialFailure` and
`RunAsync_ReplayWithAnUnparsableInputLine_ReturnsPartialFailure`.

## 6. Disk full on output

**Injected by.** A path whose parent directory does not exist. That is the portable form of the
fault, because the guard is one `catch` on `IOException` or `UnauthorizedAccessException` and a
full volume, a read-only path, a locked file and a missing directory all arrive through it.

**What the product does.** `--output`, `--diagnostics` and `--review-queue` are opened through
one guarded helper before the record loop, so an unwritable path costs nothing: no record has
been composed and no model call has been made when the run stops, which is step 77's fail fast
before work that costs time or money. `--eval-report` is guarded at its write instead, because
the report is a report on the batch and there is no earlier moment at which it could be
written; a failure there leaves `--output` complete on disk and still ends the run at exit code
1, rather than reporting a file that is not there and exiting 0.

**What the diagnostics say.** Nothing, and that is the point: the diagnostics file is one of
the streams that could not be opened. One stderr line naming the flag the caller passed is the
whole report, and each of the four names its own flag rather than a shared one:

```
Could not open --output 'no_such_dir/out.json': DirectoryNotFoundException: Could not find a part of the path '...'.
```

**Exit code.** 1, usage error. Before D64 the same path ended the process on an unhandled
exception with a stack trace and an exit code that was none of 0, 1 or 2.

**Not covered: a volume that fills after the file is opened.** The guard is at the open, so it
answers a write that could never start. A `StreamWriter` that opened cleanly and then runs out
of space throws from inside the writer, downstream of every guard in `CliRunner`, and
`Program.cs` installs no handler, so that run still ends on an unhandled exception. Producing
an actual out-of-space condition needs a virtual disk or a filesystem quota, machine setup this
suite cannot carry and CI cannot reproduce, so it is scoped out in
[CODE_REVIEW.md](CODE_REVIEW.md) (D64) rather than claimed.

**Proved by.** `tests/Agent.Cli.Tests/CliRunnerTests.cs`,
`RunAsync_OutputPathHasNoParentDirectory_WritesCleanErrorAndReturnsUsageError`,
`RunAsync_DiagnosticsPathHasNoParentDirectory_WritesCleanErrorAndReturnsUsageError`,
`RunAsync_ReviewQueuePathHasNoParentDirectory_WritesCleanErrorAndReturnsUsageError`,
`RunAsync_EvalReportPathHasNoParentDirectory_WritesCleanErrorAndReturnsUsageError`,
`RunAsync_ReplayEvalReportPathHasNoParentDirectory_WritesCleanErrorAndReturnsUsageError`, and
the guard this one copies, `RunAsync_LogFilePathHasNoParentDirectory_WritesCleanErrorAndReturnsUsageError`.
