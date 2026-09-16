"""A run as the report reads it: the scorecard, joined row by row with the run's diagnostics.

eval.json (from --eval-json) holds the label checks. diag.json holds what the run itself did per
record: latency, which composer wrote the message, safety violations and the judge's grades and
reason. The two are written in the same record order, so row i of one is row i of the other, and a
row whose task id differs is reported and not joined rather than shown against the wrong record.
review_queue.json is only counted. Every file is external input: a row that does not have the
expected shape is reported by its position and left out, and the rest of the run is still shown.
"""

import json
from dataclasses import dataclass
from enum import Enum
from pathlib import Path
from typing import Any

JUDGE_CHECKS = ("action_semantic", "body_semantic")


class ReportInputError(Exception):
    """A run directory the report cannot be built from at all."""


class CheckResult(Enum):
    PASSED = "passed"
    FAILED = "failed"
    NOT_MEASURED = "not_measured"


@dataclass(frozen=True)
class CheckTally:
    check: str
    label: str
    passed: int
    measured: int

    @property
    def failed(self) -> int:
        return self.measured - self.passed


@dataclass(frozen=True)
class ModelCost:
    calls: int
    completed_calls: int
    input_tokens: int
    output_tokens: int


@dataclass(frozen=True)
class RecordRow:
    position: int
    task_id: str
    passed: bool
    results: dict[str, CheckResult]
    scoring_error: str | None
    latency_ms: float | None
    composer: str | None
    safety_violations: int | None
    judge_reason: str | None
    model_cost: ModelCost | None = None
    judge_cost: ModelCost | None = None

    @property
    def failed_checks(self) -> list[str]:
        return [check for check, result in self.results.items() if result is CheckResult.FAILED]


@dataclass(frozen=True)
class RunReport:
    passed: int
    total: int
    checks: list[CheckTally]
    latency_p95_ms: float | None
    latency_budget_ms: int | None
    latency_p95: CheckResult
    batch_latency_ms: float | None
    batch_model_cost: ModelCost | None
    judge_model_cost: ModelCost | None
    records: list[RecordRow]
    review_queue_count: int | None
    problems: list[str]

    @property
    def is_replay(self) -> bool:
        """A scorecard rebuilt with --replay timed no batch, so it carries no batch latency."""
        return self.batch_latency_ms is None


class _RowError(Exception):
    pass


def load_run(run_dir: Path) -> RunReport:
    """O(n × c) in the rows and the checks: one pass over each file."""
    scorecard = _read_json(run_dir / "eval.json", required=True)
    if not isinstance(scorecard, dict):
        raise ReportInputError("eval.json is not a JSON object.")
    diagnostics = _read_json(run_dir / "diag.json", required=False)
    review_queue = _read_json(run_dir / "review_queue.json", required=False)

    try:
        labels = [(str(tally["check"]), str(tally["label"])) for tally in scorecard["checks"]]
        records_json = list(scorecard["records"])
    except (KeyError, TypeError) as error:
        raise ReportInputError(f"eval.json is missing its checks or records ({error}).") from error

    diagnostics_rows = diagnostics if isinstance(diagnostics, list) else []
    problems: list[str] = []
    rows: list[RecordRow] = []
    for index, row_json in enumerate(records_json):
        diagnostics_row = diagnostics_rows[index] if index < len(diagnostics_rows) else None
        try:
            rows.append(_read_row(index, row_json, diagnostics_row, problems))
        except _RowError as error:
            problems.append(str(error))

    # A scorecard rebuilt with --replay timed nothing and called no model, so its p95 and both costs
    # are empty. The live run recorded them per record on its diagnostics rows: the costs are those
    # rows summed, and the p95 is the program's own nearest-rank p95 over their latencies.
    # Only a rebuilt scorecard is filled in, and only from rows the scorecard scored: a row it could
    # not score carries no latency there, and a live scorecard's unmeasured p95 stays unmeasured.
    latency_p95_ms = scorecard.get("latency_p95_ms")
    try:
        latency_p95 = _result(scorecard.get("latency_p95", "not_measured"), "eval.json")
    except _RowError as error:
        raise ReportInputError(f"eval.json has an unknown latency_p95 '{scorecard.get('latency_p95')}'.") from error
    budget_ms = scorecard.get("latency_budget_ms")
    if latency_p95_ms is None and scorecard.get("batch_latency_ms") is None:
        latency_p95_ms = _nearest_rank_p95(
            [row.latency_ms for row in rows if row.latency_ms is not None and row.scoring_error is None]
        )
        if latency_p95_ms is not None and budget_ms is not None:
            latency_p95 = CheckResult.PASSED if latency_p95_ms <= budget_ms else CheckResult.FAILED

    return RunReport(
        passed=int(scorecard.get("passed", 0)),
        total=int(scorecard.get("total", len(records_json))),
        checks=[_tally(check, label, rows) for check, label in labels],
        latency_p95_ms=latency_p95_ms,
        latency_budget_ms=budget_ms,
        latency_p95=latency_p95,
        batch_latency_ms=scorecard.get("batch_latency_ms"),
        batch_model_cost=_cost(scorecard.get("batch_model_cost")) or _sum_costs([row.model_cost for row in rows]),
        judge_model_cost=_cost(scorecard.get("judge_model_cost")) or _sum_costs([row.judge_cost for row in rows]),
        records=rows,
        review_queue_count=len(review_queue) if isinstance(review_queue, list) else None,
        problems=problems,
    )


def _read_json(path: Path, required: bool) -> Any:
    if not path.exists():
        if required:
            raise ReportInputError(f"{path.name} was not found in {path.parent}; run with --eval-json.")
        return None
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as error:
        raise ReportInputError(f"{path.name} is not valid JSON ({error.msg}, line {error.lineno}).") from error


def _read_row(index: int, row: Any, diagnostics_row: Any, problems: list[str]) -> RecordRow:
    position = index + 1
    if not isinstance(row, dict):
        raise _RowError(f"Row {position} of eval.json is not a record object; it is left out of the report.")
    if "results" in row and not isinstance(row["results"], dict):
        raise _RowError(f"Row {position} of eval.json has results that are not an object; it is left out of the report.")
    try:
        task_id = str(row["task_id"])
        results = {check: _result(value, f"Row {position} of eval.json") for check, value in row["results"].items()}
        passed = bool(row["passed"])
    except KeyError as error:
        raise _RowError(f"Row {position} of eval.json is missing {error}; it is left out of the report.") from error

    latency_ms: float | None = row.get("latency_ms")
    judge_reason: str | None = row.get("judge_reason")
    composer: str | None = None
    safety_violations: int | None = None
    model_cost: ModelCost | None = None
    judge_cost: ModelCost | None = None

    if isinstance(diagnostics_row, dict):
        if diagnostics_row.get("task_id") != task_id:
            problems.append(
                f"Row {position}: the diagnostics row is for '{diagnostics_row.get('task_id')}', not '{task_id}'; "
                "its latency, writer and judge are not shown."
            )
        else:
            details = diagnostics_row.get("diagnostics") or {}
            composition = details.get("composition") or {}
            composer = composition.get("composer")
            safety_violations = details.get("safety_violation_count")
            model_cost = _cost(details.get("model_cost"))
            latency_ms = diagnostics_row.get("latency_ms", latency_ms)
            judge = diagnostics_row.get("judge")
            if isinstance(judge, dict):
                judge_cost = _cost(judge.get("model_cost"))
                # A scorecard rebuilt with --replay carries no judge grades; the live run's grades
                # are on its diagnostics rows, so those are the ones shown.
                judge_reason = judge.get("reason", judge_reason)
                for check in JUDGE_CHECKS:
                    if results.get(check) is CheckResult.NOT_MEASURED and check in judge:
                        results[check] = _result(judge[check], f"Row {position} of diag.json")

    return RecordRow(
        position=position,
        task_id=task_id,
        passed=passed,
        results=results,
        scoring_error=row.get("scoring_error"),
        latency_ms=latency_ms,
        composer=composer,
        safety_violations=safety_violations,
        judge_reason=judge_reason,
        model_cost=model_cost,
        judge_cost=judge_cost,
    )


def _result(value: Any, where: str) -> CheckResult:
    try:
        return CheckResult(value)
    except ValueError as error:
        raise _RowError(f"{where} has an unknown result '{value}'; it is left out of the report.") from error


def _tally(check: str, label: str, rows: list[RecordRow]) -> CheckTally:
    measured = [row.results.get(check, CheckResult.NOT_MEASURED) for row in rows]
    return CheckTally(
        check=check,
        label=label,
        passed=sum(1 for result in measured if result is CheckResult.PASSED),
        measured=sum(1 for result in measured if result is not CheckResult.NOT_MEASURED),
    )


def _nearest_rank_p95(latencies: list[float]) -> float | None:
    """Rank ceil(0.95 × n) of the sorted latencies, as Scorecard computes it. O(n log n)."""
    if not latencies:
        return None
    ordered = sorted(latencies)
    return ordered[(95 * len(ordered) + 99) // 100 - 1]


def _sum_costs(costs: list[ModelCost | None]) -> ModelCost | None:
    present = [cost for cost in costs if cost is not None]
    if not present:
        return None
    return ModelCost(
        calls=sum(cost.calls for cost in present),
        completed_calls=sum(cost.completed_calls for cost in present),
        input_tokens=sum(cost.input_tokens for cost in present),
        output_tokens=sum(cost.output_tokens for cost in present),
    )


def _cost(value: Any) -> ModelCost | None:
    if not isinstance(value, dict):
        return None
    return ModelCost(
        calls=int(value.get("calls", 0)),
        completed_calls=int(value.get("completed_calls", 0)),
        input_tokens=int(value.get("input_tokens", 0)),
        output_tokens=int(value.get("output_tokens", 0)),
    )
