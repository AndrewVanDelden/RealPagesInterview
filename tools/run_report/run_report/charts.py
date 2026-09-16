"""The report's three charts, drawn with matplotlib and returned as inline SVG text.

Colors are the data-viz reference palette's: the status colors mean passed and failed and never
stand for anything else, and every status cell or segment also carries a glyph, a count or a legend
entry, so color is never the only signal. SVG text stays text (svg.fonttype none), so the page's
own font renders it and it can be searched and read by assistive technology.
"""

import io
from typing import Literal

import matplotlib

matplotlib.use("Agg")

import matplotlib.pyplot as plt  # noqa: E402
from matplotlib.axes import Axes  # noqa: E402
from matplotlib.figure import Figure  # noqa: E402
from matplotlib.patches import Rectangle  # noqa: E402

from run_report.model import CheckResult, CheckTally, RecordRow  # noqa: E402

SURFACE = "#fcfcfb"
INK = "#0b0b0b"
INK_SECONDARY = "#52514e"
INK_MUTED = "#898781"
GRIDLINE = "#e1e0d9"
BASELINE = "#c3c2b7"
PASSED = "#0ca30c"
FAILED = "#d03b3b"
NOT_MEASURED = "#e1e0d9"
SERIES = "#2a78d6"

GLYPHS = {CheckResult.PASSED: "✓", CheckResult.FAILED: "✗", CheckResult.NOT_MEASURED: "–"}
FILLS = {CheckResult.PASSED: PASSED, CheckResult.FAILED: FAILED, CheckResult.NOT_MEASURED: NOT_MEASURED}

plt.rcParams.update(
    {
        "svg.fonttype": "none",
        # Task ids come from the input file, and a "$" in one must print as written, never be read as
        # matplotlib math, which garbles the label or raises and loses the whole report.
        "text.parse_math": False,
        "font.family": ["Segoe UI", "DejaVu Sans"],
        "font.size": 10,
        "text.color": INK,
        "axes.edgecolor": BASELINE,
        "axes.labelcolor": INK_SECONDARY,
        "xtick.color": INK_MUTED,
        "ytick.color": INK_SECONDARY,
        "figure.facecolor": SURFACE,
        "axes.facecolor": SURFACE,
    }
)


def check_tallies_svg(checks: list[CheckTally], rows: int) -> str:
    """One stacked bar per check: passed, failed, and the rows it did not measure. O(c)."""
    figure, axes = plt.subplots(figsize=(9.0, 0.42 * len(checks) + 1.2))
    positions = list(range(len(checks)))[::-1]
    for position, tally in zip(positions, checks, strict=True):
        not_measured = max(rows - tally.measured, 0)
        left = 0
        for width, color in ((tally.passed, PASSED), (tally.failed, FAILED), (not_measured, NOT_MEASURED)):
            if width > 0:
                axes.barh(position, width, left=left, height=0.62, color=color, edgecolor=SURFACE, linewidth=2)
            left += width
        axes.text(rows + rows * 0.015, position, f"{tally.passed}/{tally.measured}", va="center", color=INK, fontsize=10)

    axes.set_yticks(positions, [tally.label for tally in checks])
    axes.set_xlim(0, rows * 1.12)
    axes.set_xlabel("Records")
    _recessive(axes, grid_axis="x")
    axes.legend(
        handles=[
            Rectangle((0, 0), 1, 1, color=PASSED, label="Passed"),
            Rectangle((0, 0), 1, 1, color=FAILED, label="Failed"),
            Rectangle((0, 0), 1, 1, color=NOT_MEASURED, label="Not measured"),
        ],
        loc="lower center",
        bbox_to_anchor=(0.45, 1.0),
        ncol=3,
        frameon=False,
    )
    return _svg(figure)


def record_grid_svg(records: list[RecordRow], checks: list[CheckTally]) -> str:
    """Every record's result on every check, one cell each, with a glyph in the cell. O(n × c)."""
    figure, axes = plt.subplots(figsize=(10.5, 0.27 * len(records) + 1.4))
    for row_index, row in enumerate(records):
        for column, tally in enumerate(checks):
            result = row.results.get(tally.check, CheckResult.NOT_MEASURED)
            axes.add_patch(
                Rectangle((column, row_index), 1, 1, facecolor=FILLS[result], edgecolor=SURFACE, linewidth=2)
            )
            glyph_color = INK_MUTED if result is CheckResult.NOT_MEASURED else SURFACE
            axes.text(column + 0.5, row_index + 0.52, GLYPHS[result], ha="center", va="center", color=glyph_color, fontsize=8)

    axes.set_xlim(0, len(checks))
    axes.set_ylim(len(records), 0)
    axes.set_xticks([column + 0.5 for column in range(len(checks))], [tally.label for tally in checks], rotation=35, ha="left")
    axes.xaxis.tick_top()
    axes.set_yticks(
        [index + 0.5 for index in range(len(records))],
        [f"{row.position:02d}  {_shorten(row.task_id, 46)}" for row in records],
        fontsize=8,
    )
    for side in axes.spines.values():
        side.set_visible(False)
    axes.tick_params(length=0)
    return _svg(figure)


def latency_svg(records: list[RecordRow], budget_ms: int | None, p95_ms: float | None) -> str:
    """Each measured record's latency in scorecard order, against the budget and the batch p95. O(n).

    Both reference lines are labelled in the right margin, clear of the bars.
    """
    measured = [row for row in records if row.latency_ms is not None]
    figure, axes = plt.subplots(figsize=(9.0, 3.4))
    positions = [row.position for row in measured]
    seconds = [(row.latency_ms or 0.0) / 1000 for row in measured]
    axes.bar(positions, seconds, width=0.7, color=SERIES, edgecolor=SURFACE, linewidth=1)

    if budget_ms is not None:
        axes.axhline(budget_ms / 1000, color=FAILED, linewidth=1.5, linestyle="--")
        axes.text(1.005, budget_ms / 1000, f"budget {budget_ms / 1000:.1f} s", transform=axes.get_yaxis_transform(), va="center", color=FAILED, fontsize=9)
    if p95_ms is not None:
        axes.axhline(p95_ms / 1000, color=INK, linewidth=1, linestyle=":")
        axes.text(1.005, p95_ms / 1000, f"p95 {p95_ms / 1000:.1f} s", transform=axes.get_yaxis_transform(), va="center", color=INK, fontsize=9)

    axes.set_xlabel("Record (scorecard order)")
    axes.set_ylabel("Seconds")
    axes.set_xlim(0.3, max(positions, default=1) + 0.7)
    _recessive(axes, grid_axis="y")
    return _svg(figure)


def _recessive(axes: Axes, grid_axis: Literal["x", "y"]) -> None:
    axes.grid(axis=grid_axis, color=GRIDLINE, linewidth=0.8)
    axes.set_axisbelow(True)
    for name in ("top", "right"):
        axes.spines[name].set_visible(False)
    axes.tick_params(length=0)


def _shorten(text: str, limit: int) -> str:
    return text if len(text) <= limit else text[: limit - 1] + "…"


def _svg(figure: Figure) -> str:
    buffer = io.StringIO()
    figure.savefig(buffer, format="svg", bbox_inches="tight")
    plt.close(figure)
    text = buffer.getvalue()
    return text[text.index("<svg") :]
