"""What the report reads from a run's files, and what it refuses."""

import json
from pathlib import Path
from typing import Any

import pytest

from run_report.model import CheckResult, ReportInputError, load_run
from tests.conftest import diagnostics_row, record, scorecard


def test_load_run_reads_totals_tallies_and_rows_from_the_scorecard(write_run: Any) -> None:
    run_dir: Path = write_run(
        scorecard([record("t1"), record("t2", passed=False, send_at_day="failed", cta_payload="not_measured")]),
        [diagnostics_row("t1"), diagnostics_row("t2")],
        [{"task_id": "t2"}],
    )

    run = load_run(run_dir)

    assert (run.passed, run.total) == (1, 2)
    day = next(tally for tally in run.checks if tally.check == "send_at_day")
    assert (day.label, day.passed, day.measured) == ("Day", 1, 2)
    assert run.records[1].results["send_at_day"] is CheckResult.FAILED
    assert run.records[1].results["cta_payload"] is CheckResult.NOT_MEASURED
    assert run.records[1].failed_checks == ["send_at_day"]
    assert run.review_queue_count == 1
    assert run.problems == []


def test_load_run_takes_latency_writer_and_judge_from_the_diagnostics_row_at_the_same_position(write_run: Any) -> None:
    run_dir: Path = write_run(
        scorecard([record("t1"), record("t2")]),
        [diagnostics_row("t1", composer="template"), diagnostics_row("t2", composer=None, judge_reason="Different days.")],
    )

    run = load_run(run_dir)

    assert run.records[0].composer == "template"
    assert run.records[0].latency_ms == 1700.0
    assert run.records[1].composer is None
    assert run.records[1].judge_reason == "Different days."
    assert run.records[1].safety_violations == 0


def test_load_run_uses_the_live_runs_judge_grades_when_the_scorecard_was_rebuilt_without_them(write_run: Any) -> None:
    replayed = record("t1", action_semantic="not_measured", body_semantic="not_measured")
    run_dir: Path = write_run(scorecard([replayed], batch_latency_ms=None), [diagnostics_row("t1", judge_reason="Other days.")])

    run = load_run(run_dir)

    assert run.records[0].results["action_semantic"] is CheckResult.PASSED
    assert run.records[0].results["body_semantic"] is CheckResult.FAILED
    body = next(tally for tally in run.checks if tally.check == "body_semantic")
    assert (body.passed, body.measured) == (0, 1)
    assert run.is_replay


def test_load_run_takes_cost_and_p95_from_the_diagnostics_when_the_scorecard_was_rebuilt(write_run: Any) -> None:
    replayed = scorecard([record(f"t{index}") for index in range(1, 21)], batch_latency_ms=None)
    replayed["batch_model_cost"] = None
    replayed["latency_p95_ms"] = None
    replayed["latency_p95"] = "not_measured"
    rows = [diagnostics_row(f"t{index}", judge_reason="r") for index in range(1, 21)]
    for index, row in enumerate(rows, start=1):
        row["latency_ms"] = float(index * 100)
        row["diagnostics"]["model_cost"] = {"calls": 1, "completed_calls": 1, "input_tokens": 10, "output_tokens": 2}
        row["judge"]["model_cost"] = {"calls": 1, "completed_calls": 1, "input_tokens": 5, "output_tokens": 1}

    run = load_run(write_run(replayed, rows))

    # Nearest rank, the program's own p95: rank ceil(0.95 × 20) = 19 of 100 ms steps.
    assert run.latency_p95_ms == 1900.0
    assert run.latency_p95 is CheckResult.PASSED
    assert run.batch_model_cost is not None and (run.batch_model_cost.calls, run.batch_model_cost.input_tokens) == (20, 200)
    assert run.judge_model_cost is not None and run.judge_model_cost.output_tokens == 20


def test_load_run_leaves_a_row_without_a_diagnostics_row_unjoined(write_run: Any) -> None:
    unparsed = record("(did not parse)", passed=False)
    unparsed["scoring_error"] = "Line 2 failed to parse"
    unparsed["latency_ms"] = None
    run_dir: Path = write_run(scorecard([record("t1"), unparsed]), [diagnostics_row("t1")])

    run = load_run(run_dir)

    assert run.records[1].latency_ms is None
    assert run.records[1].scoring_error == "Line 2 failed to parse"


def test_load_run_reports_a_diagnostics_row_for_another_task_as_a_problem_and_does_not_join_it(write_run: Any) -> None:
    run_dir: Path = write_run(scorecard([record("t1")]), [diagnostics_row("other")])

    run = load_run(run_dir)

    assert run.records[0].composer is None
    assert run.problems == ["Row 1: the diagnostics row is for 'other', not 't1'; its latency, writer and judge are not shown."]


def test_load_run_reports_a_malformed_row_by_position_and_keeps_every_other_row(write_run: Any) -> None:
    broken: dict[str, Any] = record("t2")
    del broken["results"]
    run_dir: Path = write_run(scorecard([record("t1"), broken, record("t3")]))

    run = load_run(run_dir)

    assert [row.task_id for row in run.records] == ["t1", "t3"]
    assert run.problems == ["Row 2 of eval.json is missing 'results'; it is left out of the report."]


def test_load_run_without_diagnostics_or_review_queue_still_reads_the_scorecard(write_run: Any) -> None:
    run = load_run(write_run(scorecard([record("t1")])))

    assert run.records[0].composer is None
    assert run.review_queue_count is None


def test_load_run_without_a_scorecard_raises_naming_the_file(write_run: Any) -> None:
    run_dir: Path = write_run(None)

    with pytest.raises(ReportInputError, match="eval.json"):
        load_run(run_dir)


def test_load_run_with_a_scorecard_that_is_not_json_raises_naming_the_file(write_run: Any) -> None:
    run_dir: Path = write_run(None)
    (run_dir / "eval.json").write_text("{not json", encoding="utf-8")

    with pytest.raises(ReportInputError, match="eval.json is not valid JSON"):
        load_run(run_dir)


def test_load_run_with_an_unknown_result_value_reports_the_row(write_run: Any) -> None:
    run_dir: Path = write_run(scorecard([record("t1", channel="maybe")]))

    run = load_run(run_dir)

    assert run.records == []
    assert run.problems == ["Row 1 of eval.json has an unknown result 'maybe'; it is left out of the report."]
    assert json.loads((run_dir / "eval.json").read_text(encoding="utf-8"))["total"] == 1
