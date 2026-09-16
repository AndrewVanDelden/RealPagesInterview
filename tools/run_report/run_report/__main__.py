"""python -m run_report --run-dir <dir> [--out <file.html>]

Builds the report from a run directory's eval.json, diag.json and review_queue.json and prints the
path of the page it wrote. Exit code 0 when the page is written, 1 when the run directory cannot be
read, with one line on stderr saying why.
"""

import argparse
import sys
from pathlib import Path

from run_report.model import ReportInputError, load_run
from run_report.page import render_page


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(prog="run_report", description="Build the visual report for one run.")
    parser.add_argument("--run-dir", required=True, type=Path, help="the folder holding eval.json and diag.json")
    parser.add_argument("--out", type=Path, default=None, help="where to write the page (default: <run-dir>/report.html)")
    arguments = parser.parse_args(argv)

    run_dir: Path = arguments.run_dir
    out: Path = arguments.out or run_dir / "report.html"
    try:
        run = load_run(run_dir)
    except ReportInputError as error:
        print(f"Could not build the report: {error}", file=sys.stderr)
        return 1

    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(render_page(run, str(run_dir.resolve())), encoding="utf-8")
    print(out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
