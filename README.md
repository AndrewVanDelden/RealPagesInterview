# Next-Best-Message Agent

RealPage take-home assessment: a context-aware message-sending agent that
decides whether to communicate, how, when, and what to say from a JSONL
input record, then emits structured output that semantically matches the
expected result.

- Design and architecture: [docs/DESIGN.md](docs/DESIGN.md)
- Epic and sprint plan: [docs/BACKLOG.md](docs/BACKLOG.md)
- Sprint-by-sprint decision log: [TalkingPoints.md](TalkingPoints.md)

## Layout

- `src/Agent` - domain logic and pipeline, class library.
- `src/Agent.Cli` - console entry point, thin shell over `Agent`.
- `tests/Agent.Tests` - xUnit tests for `Agent`.

## Build

```
dotnet build
```
