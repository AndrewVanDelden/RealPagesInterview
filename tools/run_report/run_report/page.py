"""The report page: one self-contained HTML file with the totals first, then the charts and tables.

Every value that came from a run's files, task ids and judge reasons included, is HTML-escaped
before it is written: a record's text is data and never markup. The charts are drawn on the light
chart surface in both themes, so their colors stay the validated light steps; the page around
them follows the viewer's light or dark setting.
"""

from html import escape

from run_report.charts import check_tallies_svg, latency_svg, record_grid_svg
from run_report.model import CheckResult, ModelCost, RecordRow, RunReport

STYLE = """
:root { color-scheme: light; --page: #f9f9f7; --surface: #fcfcfb; --ink: #0b0b0b; --ink-2: #52514e;
  --muted: #898781; --line: #e1e0d9; --good: #006300; --bad: #d03b3b; }
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { color-scheme: dark; --page: #0d0d0d;
  --surface: #1a1a19; --ink: #ffffff; --ink-2: #c3c2b7; --muted: #898781; --line: #2c2c2a; --good: #0ca30c; --bad: #e66767; } }
:root[data-theme="dark"] { color-scheme: dark; --page: #0d0d0d; --surface: #1a1a19; --ink: #ffffff;
  --ink-2: #c3c2b7; --muted: #898781; --line: #2c2c2a; --good: #0ca30c; --bad: #e66767; }
* { box-sizing: border-box; }
body { margin: 0; background: var(--page); color: var(--ink); font: 15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; }
main { max-width: 1120px; margin: 0 auto; padding: 32px 16px 64px; }
h1 { font-size: 26px; margin: 0 0 4px; } h2 { font-size: 19px; margin: 40px 0 8px; }
.sub { color: var(--ink-2); margin: 0; } .note { color: var(--ink-2); max-width: 72ch; }
.kpis { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 12px; margin-top: 24px; }
.kpi { background: var(--surface); border: 1px solid var(--line); border-radius: 10px; padding: 14px 16px; }
.kpi .label { color: var(--ink-2); font-size: 13px; } .kpi .value { font-size: 28px; font-weight: 600; margin-top: 2px; }
.kpi .detail { color: var(--muted); font-size: 13px; }
.good { color: var(--good); } .bad { color: var(--bad); }
figure { margin: 12px 0 0; background: #fcfcfb; border: 1px solid var(--line); border-radius: 10px; padding: 12px; overflow-x: auto; }
figure svg { max-width: 100%; height: auto; display: block; }
table { border-collapse: collapse; width: 100%; background: var(--surface); font-size: 14px; }
th, td { text-align: left; padding: 8px 10px; border-bottom: 1px solid var(--line); vertical-align: top; }
th { color: var(--ink-2); font-weight: 600; } td.num, th.num { text-align: right; font-variant-numeric: tabular-nums; }
.wrap { overflow-x: auto; border: 1px solid var(--line); border-radius: 10px; margin-top: 12px; }
details { margin-top: 10px; } summary { cursor: pointer; color: var(--ink-2); }
code { font-size: 13px; }
"""


def render_page(run: RunReport, run_label: str) -> str:
    """O(n × c) in the records and checks, the size of the grid it draws."""
    failed_rows = [row for row in run.records if not row.passed]
    labels = {tally.check: tally.label for tally in run.checks}
    sections = [
        f"<h1>Run report</h1><p class=\"sub\">{escape(run_label)}</p>",
        _replay_note(run),
        _kpis(run),
        "<h2>Checks</h2><p class=\"note\">Each bar is every record: passed, failed, or not measured for that check. "
        "The number is passed over measured.</p>",
        f"<figure>{check_tallies_svg(run.checks, len(run.records))}</figure>",
        _tally_table(run),
        "<h2>Every record</h2><p class=\"note\">One row per record in input order, one column per check. "
        "✓ passed, ✗ failed, – not measured.</p>",
        f"<figure>{record_grid_svg(run.records, run.checks)}</figure>",
        "<h2>Latency</h2><p class=\"note\">Each record's time from start to its decision, including every model call "
        "and any wait for the rate limit.</p>",
        f"<figure>{latency_svg(run.records, run.latency_budget_ms, run.latency_p95_ms)}</figure>",
        f"<h2>Failed records ({len(failed_rows)})</h2>",
        _failures_table(failed_rows, labels),
        _problems(run.problems),
    ]
    body = "\n".join(section for section in sections if section)
    return (
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">"
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
        f"<title>Run report</title><style>{STYLE}</style></head><body><main>{body}</main></body></html>"
    )


def _replay_note(run: RunReport) -> str:
    if not run.is_replay:
        return ""
    return (
        "<p class=\"note\">This scorecard was rebuilt with --replay from the run's saved output, so the checks only a live "
        "run measures (Safety, and latency on the scorecard) read not measured. Latency, the message writer, safety "
        "violations and the judge's grades below come from the live run's diagnostics.</p>"
    )


def _kpis(run: RunReport) -> str:
    worst = min(
        (tally for tally in run.checks if tally.measured > 0),
        key=lambda tally: tally.passed / tally.measured,
        default=None,
    )
    writers: dict[str, int] = {}
    for row in run.records:
        writer = row.composer or "no message"
        writers[writer] = writers.get(writer, 0) + 1
    violations = [row.safety_violations for row in run.records if row.safety_violations is not None]

    tiles = [
        _tile("Records passed", f"{run.passed} of {run.total}", "every label check on the record passed", run.passed == run.total),
        _tile(
            "Weakest check",
            f"{escape(worst.label)} {worst.passed}/{worst.measured}" if worst else "none measured",
            "lowest pass rate among measured checks",
            None,
        ),
        _tile("Latency p95", _seconds(run.latency_p95_ms), _budget_detail(run), run.latency_p95 is CheckResult.PASSED
              if run.latency_p95 is not CheckResult.NOT_MEASURED else None),
        _tile("Message writer", ", ".join(f"{count} {escape(name)}" for name, count in sorted(writers.items())), "per record", None),
        _tile("Safety violations", str(sum(violations)) if violations else "not recorded", "across all records", not any(violations) if violations else None),
        _tile("Review queue", "not written" if run.review_queue_count is None else str(run.review_queue_count), "records a person should look at", None),
        _tile("Product tokens", _tokens(run.batch_model_cost), _calls(run.batch_model_cost), None),
        _tile("Judge tokens", _tokens(run.judge_model_cost), _calls(run.judge_model_cost), None),
    ]
    return f"<div class=\"kpis\">{''.join(tiles)}</div>"


def _tile(label: str, value: str, detail: str, good: bool | None) -> str:
    tone = "" if good is None else (" good" if good else " bad")
    return (
        f"<div class=\"kpi\"><div class=\"label\">{escape(label)}</div>"
        f"<div class=\"value{tone}\">{value}</div><div class=\"detail\">{escape(detail)}</div></div>"
    )


def _tally_table(run: RunReport) -> str:
    rows = "".join(
        f"<tr><td>{escape(tally.label)}</td><td class=\"num\">{tally.passed}</td><td class=\"num\">{tally.failed}</td>"
        f"<td class=\"num\">{len(run.records) - tally.measured}</td></tr>"
        for tally in run.checks
    )
    return (
        "<details><summary>Checks as a table</summary><div class=\"wrap\"><table><thead><tr><th>Check</th>"
        "<th class=\"num\">Passed</th><th class=\"num\">Failed</th><th class=\"num\">Not measured</th></tr></thead>"
        f"<tbody>{rows}</tbody></table></div></details>"
    )


def _failures_table(rows: list[RecordRow], labels: dict[str, str]) -> str:
    if not rows:
        return "<p class=\"note\">No record failed a label check.</p>"
    body = "".join(
        f"<tr><td class=\"num\">{row.position}</td><td><code>{escape(row.task_id)}</code></td>"
        f"<td>{escape(row.scoring_error) if row.scoring_error else escape(', '.join(labels.get(check, check) for check in row.failed_checks))}</td>"
        f"<td>{escape(row.composer or 'no message')}</td><td>{escape(row.judge_reason or '')}</td></tr>"
        for row in rows
    )
    return (
        "<div class=\"wrap\"><table><thead><tr><th class=\"num\">#</th><th>Task</th><th>Failed checks</th>"
        f"<th>Writer</th><th>Judge's reason</th></tr></thead><tbody>{body}</tbody></table></div>"
    )


def _problems(problems: list[str]) -> str:
    if not problems:
        return ""
    items = "".join(f"<li>{escape(problem)}</li>" for problem in problems)
    return f"<h2>Problems in the run's files</h2><ul>{items}</ul>"


def _seconds(milliseconds: float | None) -> str:
    return "not measured" if milliseconds is None else f"{milliseconds / 1000:.1f} s"


def _budget_detail(run: RunReport) -> str:
    if run.latency_budget_ms is None:
        return "no budget stated"
    verdict = {CheckResult.PASSED: "within", CheckResult.FAILED: "over"}.get(run.latency_p95, "not judged against")
    return f"{verdict} the {run.latency_budget_ms / 1000:.1f} s budget"


def _tokens(cost: ModelCost | None) -> str:
    return "none" if cost is None else f"{cost.input_tokens + cost.output_tokens:,}"


def _calls(cost: ModelCost | None) -> str:
    if cost is None:
        return "no model call"
    return f"{cost.input_tokens:,} in, {cost.output_tokens:,} out; {cost.completed_calls} of {cost.calls} calls completed"
