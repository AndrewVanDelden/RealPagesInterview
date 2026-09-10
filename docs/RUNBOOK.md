# Runbook

From the repository root, in PowerShell, with the .NET 10 SDK. Every file the runs write is git-ignored.

1. Set the secret, only for `--composer openai` or `--judge`; nothing below needs it. Put your OpenAI key in place of `<key>`.
```bash
dotnet user-secrets set "OpenAI:ApiKey" "<key>" --project src/Agent.Cli
```

2. Build, then test. `.\test.ps1` exits 0 only when every test passes at 100 percent coverage.
```bash
dotnet build
```
```bash
.\test.ps1
```

3. Run the hold-out set at its documented reference time. It exits 0 and writes `out.json`, `eval.txt` and `run.log`. The synthetic set exits 2 by design: its line 11 is malformed.
```bash
dotnet run --project src/Agent.Cli -- --input holdout_12.jsonl --output out.json --now 2025-12-09T00:00:00-06:00 --eval-report eval.txt --log-file run.log
```
```bash
dotnet run --project src/Agent.Cli -- --input synthetic_12.jsonl --output out-synthetic.json --now 2026-03-07T12:00:00Z --eval-report eval-synthetic.txt
```

4. Open the scorecard: one row per record, `OK`, `FAIL` or `n/a` (not measured) per check, then `Checks:` (passed over measured, per check) and `Overall:`. The hold-out reads `Overall: 4/12 passed`; `eval-synthetic.txt` reads `Overall: 12/13 passed`.
```bash
cat eval.txt
```

5. Read a log line in `run.log`, which every run appends to; stderr carries the same lines. One from the run above:
```
2026-09-10T17:11:34.8225092+00:00 [Information] Agent.Orchestration.LeasingMessageAgent: Message composed: channel=Sms, nextAction=start_cadence. TaskId=prospect_welcome_day0
```
Left to right: the UTC timestamp, the level (`Warning` is a handled retry or fallback, `Error` is something lost), the class that logged it, the message, then `TaskId`, the record's correlation scope. Search the log for one `TaskId` to get that record's whole story. The format is [OPERATIONS.md](OPERATIONS.md) section 4.

6. Replay: re-score `out.json` from step 3 without running the agent. It exits 0 with the same tallies, except Safety reads 0/0 and latency `n/a`, which exist only in the run that wrote the file.
```bash
dotnet run --project src/Agent.Cli -- --input holdout_12.jsonl --replay out.json --eval-report eval-replay.txt
```

An evaluation run with the model, `--composer openai`, needs `--model-call-budget-ms` for the model to answer more than the few records whose completion fits the records' 2000 ms: see [OPERATIONS.md](OPERATIONS.md) section 1. Exit codes 0, 1 and 2 are success, usage error and partial failure (section 2).
