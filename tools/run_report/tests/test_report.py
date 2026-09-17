"""The charts, the page and the command that writes it."""

from pathlib import Path
from typing import Any

import pytest

from run_report.__main__ import main
from run_report.charts import check_tallies_svg, latency_svg, record_grid_svg
from run_report.model import load_run
from run_report.page import render_page
from tests.conftest import diagnostics_row, record, scorecard


def _run(write_run: Any) -> Any:
    failing = record("prospect_<b>bold</b>", passed=False, send_at_day="failed", send_at_hour="failed")
    return load_run(
        write_run(
            scorecard([record("t1"), failing]),
            [diagnostics_row("t1"), diagnostics_row("prospect_<b>bold</b>", judge_reason="Offers <script>x</script> days.")],
            [{"task_id": "prospect_<b>bold</b>"}],
        )
    )


def test_check_tallies_svg_names_every_check_and_its_passed_count(write_run: Any) -> None:
    run = _run(write_run)

    svg = check_tallies_svg(run.checks, len(run.records))

    assert svg.startswith("<svg") or "<svg" in svg[:400]
    assert "Day" in svg and "BodySem" in svg
    assert "1/2" in svg
    assert "Passed" in svg and "Failed" in svg and "Not measured" in svg


def test_record_grid_svg_marks_each_cell_with_a_glyph_as_well_as_a_color(write_run: Any) -> None:
    run = _run(write_run)

    svg = record_grid_svg(run.records, run.checks)

    assert "✗" in svg and "✓" in svg
    assert "t1" in svg


def test_record_grid_svg_shows_a_task_id_with_dollar_signs_as_written(write_run: Any) -> None:
    run = load_run(write_run(scorecard([record("unit_4$B_vs_$^"), record("probe$\\alpha x$")])))

    svg = record_grid_svg(run.records, run.checks)

    assert "unit_4$B_vs_$^" in svg
    assert "probe$\\alpha x$" in svg


def test_latency_svg_draws_the_budget_and_the_p95_as_labelled_lines(write_run: Any) -> None:
    run = _run(write_run)

    svg = latency_svg(run.records, run.latency_budget_ms, run.latency_p95_ms)

    assert "budget 2.0 s" in svg
    assert "p95 1.5 s" in svg


def test_render_page_leads_with_the_totals_and_escapes_every_value_from_the_run(write_run: Any) -> None:
    run = _run(write_run)

    page = render_page(run, "runs/step99b")

    assert "1 of 2" in page
    assert "<script>x</script>" not in page
    assert "Offers &lt;script&gt;x&lt;/script&gt; days." in page
    assert "prospect_&lt;b&gt;bold&lt;/b&gt;" in page
    assert "Day, Hour" in page
    assert "scorecard order" in page
    assert "Record (input order)" not in page
    assert page.count("<svg") == 3


def test_render_page_for_an_empty_run_does_not_style_records_passed_as_good(write_run: Any) -> None:
    run = load_run(write_run(scorecard([])))

    page = render_page(run, "runs/empty")

    assert '<div class="value good">0 of 0</div>' not in page
    assert '<div class="value">0 of 0</div>' in page


def test_render_page_for_a_rebuilt_scorecard_says_which_checks_it_could_not_measure(write_run: Any) -> None:
    run = load_run(write_run(scorecard([record("t1", safety="not_measured")], batch_latency_ms=None), [diagnostics_row("t1")]))

    page = render_page(run, "runs/x")

    assert "rebuilt with --replay" in page


def test_render_page_lists_the_problems_found_in_the_files(write_run: Any) -> None:
    run = load_run(write_run(scorecard([record("t1")]), [diagnostics_row("other")]))

    page = render_page(run, "runs/x")

    assert "the diagnostics row is for &#x27;other&#x27;" in page


def test_main_writes_the_report_beside_the_run_and_prints_its_path(write_run: Any, capsys: pytest.CaptureFixture[str]) -> None:
    run_dir: Path = write_run(scorecard([record("t1")]), [diagnostics_row("t1")])

    exit_code = main(["--run-dir", str(run_dir)])

    assert exit_code == 0
    report = run_dir / "report.html"
    assert report.exists()
    assert str(report) in capsys.readouterr().out


def test_main_with_no_scorecard_exits_one_naming_the_missing_file(write_run: Any, capsys: pytest.CaptureFixture[str]) -> None:
    run_dir: Path = write_run(None)

    exit_code = main(["--run-dir", str(run_dir), "--out", str(run_dir / "elsewhere.html")])

    assert exit_code == 1
    assert "eval.json" in capsys.readouterr().err
    assert not (run_dir / "elsewhere.html").exists()
