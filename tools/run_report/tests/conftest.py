"""Builders for the three files a run writes, so each test states only what it is about."""

import json
from pathlib import Path
from typing import Any

import pytest

CHECKS: list[tuple[str, str]] = [
    ("channel", "Channel"),
    ("send_at_day", "Day"),
    ("send_at_hour", "Hour"),
    ("next_action_match", "Action"),
    ("opt_out", "OptOut"),
    ("cta_type", "CTA"),
    ("cta_payload", "Payload"),
    ("body_language", "Lang"),
    ("safety", "Safety"),
    ("personalization", "Personalization"),
    ("action_semantic", "ActionSem"),
    ("body_semantic", "BodySem"),
]


def record(task_id: str, passed: bool = True, **results: str) -> dict[str, Any]:
    all_results = {check: "passed" for check, _ in CHECKS}
    all_results.update(results)
    return {
        "task_id": task_id,
        "passed": passed,
        "results": all_results,
        "personalization_score": 1.0,
        "latency_ms": 1500.0,
        "scoring_error": None,
        "judge_reason": None,
    }


def _has_results(row: Any) -> bool:
    return isinstance(row, dict) and isinstance(row.get("results"), dict)


def scorecard(records: list[Any], batch_latency_ms: float | None = 3000.0) -> dict[str, Any]:
    return {
        "passed": sum(1 for row in records if isinstance(row, dict) and row["passed"]),
        "total": len(records),
        "checks": [
            {
                "check": check,
                "label": label,
                "passed": sum(1 for row in records if _has_results(row) and row["results"][check] == "passed"),
                "measured": sum(1 for row in records if _has_results(row) and row["results"][check] != "not_measured"),
            }
            for check, label in CHECKS
        ],
        "latency_p95_ms": 1500.0,
        "latency_budget_ms": 2000,
        "latency_p95": "passed",
        "batch_latency_ms": batch_latency_ms,
        "batch_model_cost": {"calls": 2, "completed_calls": 2, "input_tokens": 900, "output_tokens": 120},
        "judge_model_cost": None,
        "records": records,
    }


def diagnostics_row(task_id: str, composer: str | None = "openai", judge_reason: str | None = None) -> dict[str, Any]:
    return {
        "task_id": task_id,
        "diagnostics": {
            "safety_violation_count": 0,
            "composition": None if composer is None else {"composer": composer, "attempts": 1, "locale_applied": True},
        },
        "latency_ms": 1700.0,
        "judge": None
        if judge_reason is None
        else {"action_semantic": "passed", "body_semantic": "failed", "reason": judge_reason, "model_cost": None},
    }


@pytest.fixture
def write_run(tmp_path: Path) -> Any:
    def write(
        eval_json: dict[str, Any] | None,
        diag_json: list[dict[str, Any]] | None = None,
        review_queue: list[dict[str, Any]] | None = None,
    ) -> Path:
        if eval_json is not None:
            (tmp_path / "eval.json").write_text(json.dumps(eval_json), encoding="utf-8")
        if diag_json is not None:
            (tmp_path / "diag.json").write_text(json.dumps(diag_json), encoding="utf-8")
        if review_queue is not None:
            (tmp_path / "review_queue.json").write_text(json.dumps(review_queue), encoding="utf-8")
        return tmp_path

    return write
