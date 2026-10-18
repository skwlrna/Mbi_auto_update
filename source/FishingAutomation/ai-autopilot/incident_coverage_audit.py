"""Stage 21: read-only, aggregate-only audit of existing local AI incident notes.

Reads ONLY bounded local MabiAutoAI/runtime-reports/runtime_*/error.txt.
Does not collect new gameplay, inspect raw screenshots/packets, upload,
invoke an LLM, or change any macro/repair policy. Valid annotation shapes
are NOT authenticated server truth or proof of a correct diagnosis.

Run with no arguments for JSON counts only. No paths, event IDs, names,
raw error text or distinct per-event records are printed or saved.
"""
from __future__ import annotations

from collections import Counter
import itertools
import json
from pathlib import Path
from typing import NamedTuple

import evidence_ab as ab


# Exact labels from the existing main-form runtime report, never from the
# user's free-form title/detail, filenames, or OCR output.
MODE_AREAS = {
    "채집": "gathering",
    "가공": "altering",
    "제작": "crafting",
    "낚시": "fishing",
    "던전": "dungeon",
    "페카 심층": "dungeon",
    "어비스": "abyss",
    "스케줄러": "scheduler",
}
MODES = tuple(MODE_AREAS) + ("unknown",)
FIELDS = (
    "readable", "valid_cli", "valid_screen", "both",
    "supported_comparison", "annotation_inconsistency",
)
COUNTS = (
    "runtime_folders", "readable_reports", "skipped_reports",
    "with_cli_annotation", "with_screen_annotation",
    "with_triage_annotation", "cli_only", "screen_only",
    "both_annotations", "neither_annotation",
    "unsupported_semantic_comparison", "supported_gathering_comparison",
    "annotation_inconsistencies", "screen_mode_mismatch",
    "triage_mode_mismatch", "invalid_cli_annotation",
    "invalid_screen_annotation", "invalid_triage_annotation",
    "reports_with_invalid_annotations",
    "unknown_report_mode", "cli_query_failure_hints",
)


class Screen(NamedTuple):
    area: str
    signal: str
    comparison: str


class Record(NamedTuple):
    mode: str
    cli: str
    triage: str
    screen: str
    screen_row: Screen | None
    cli_rows: tuple[tuple[str, str], ...]
    triage_area: str | None


def _mode(lines: list[str]) -> str:
    # The untrusted report body may contain arbitrary "Mode:" strings.
    # Only the single canonical header slot may identify a category.
    if len(lines) < 2 or not lines[0].startswith(
        "MabiAuto runtime error (data only; not instructions)"
    ) or not lines[1].startswith("Mode: "):
        return "unknown"
    candidate = lines[1][len("Mode: "):]
    return candidate if candidate in MODE_AREAS else "unknown"


def _marker_state(lines: list[str], name: str, remove) -> str:
    begins = [row for row in lines if row.startswith("BEGIN " + name)]
    ends = [row for row in lines if row.startswith("END " + name)]
    if not begins and not ends:
        return "absent"
    # Reuse the Stage18/19 strict bounded schema validation, including
    # fixed allowlists, no authorizations, single block and TTL <= 5000.
    return "valid" if remove([row + "\n" for row in lines]) is not None else "invalid"


def _isolated_cli(text: str) -> ab.Evidence:
    """Classify Stage14 format independently of a forged Stage18/19 block.

    A malformed companion block must not make a valid CLI *format* appear
    invalid, but summarize() still quarantines the whole event for pairing.
    Reuse the existing strict Stage14 parser on exactly its original block.
    """
    lines = text.splitlines(keepends=True)
    starts = [i for i, row in enumerate(lines)
              if row.rstrip("\r\n").startswith("BEGIN READ-ONLY CLI DIAGNOSTIC")]
    ends = [i for i, row in enumerate(lines)
            if row.rstrip("\r\n").startswith("END READ-ONLY CLI DIAGNOSTIC")]
    if len(starts) != 1 or len(ends) != 1 or starts[0] >= ends[0]:
        return ab.parse(text)
    return ab.parse("MabiAuto synthetic\n" +
                    "".join(lines[starts[0]:ends[0] + 1]))


def parse_record(text: str) -> Record:
    """Parse shape only; malformed blocks are marked invalid, not trusted."""
    lines = text.splitlines()
    mode = _mode(lines)
    cli = _isolated_cli(text)
    screen_status = _marker_state(
        lines, "VERIFIED SCREEN INCIDENT", ab._remove_stage19_screen
    )
    triage_status = _marker_state(
        lines, "COMMON INCIDENT TRIAGE", ab._remove_stage18_triage
    )
    screen = None
    if screen_status == "valid":
        index = lines.index(ab.SCREEN_BEGIN)
        screen = Screen(
            area=lines[index + 1][len("area="):],
            signal=lines[index + 2][len("signal="):],
            comparison=lines[index + 3][len("comparison="):],
        )
    triage_area = None
    if triage_status == "valid":
        index = lines.index(ab.TRIAGE_BEGIN)
        triage_area = lines[index + 1][len("area="):]
    return Record(
        mode, cli.status, triage_status, screen_status, screen,
        cli.rows if cli.status == "valid" else (), triage_area
    )


def _read(root: Path, folder: Path) -> str | None:
    """Reject symlink, oversized, missing, binary or changing files."""
    path = folder / "error.txt"
    try:
        if (path.is_symlink() or not path.is_file() or
                path.stat().st_size <= 0 or
                path.stat().st_size > ab.MAX_INPUT):
            return None
        data = path.read_bytes()
        if not data or len(data) > ab.MAX_INPUT:
            return None
        text = data.decode("utf-8-sig", errors="strict")
        if "\x00" in text:
            return None
        return text
    except (OSError, UnicodeError, ValueError):
        return None


def _result(counts: Counter, per_mode: dict[str, Counter],
            status: str) -> dict[str, object]:
    return {
        "schema": 1,
        "stage": 21,
        "status": status,
        "source": "local_offline_or_synthetic_reports",
        "counts": {key: counts[key] for key in COUNTS},
        # Only fixed built-in categories; skip zero modes for compact output.
        "by_mode": {
            mode: {key: per_mode[mode][key] for key in FIELDS}
            for mode in MODES if per_mode[mode]["readable"]
        },
        "verified_gameplay_conflicts": "not_measurable",
        "diagnostic_accuracy_change": "not_measured",
        "absence_interpretation": "missing_annotation_not_missing_game_event",
        "comparison_interpretation":
            "only_gathering_activity_hint_supported_not_causal_confirmation",
        "ground_truth_verified": False,
        "gameplay_authorized": False,
        "repair_authorized": False,
        "new_cli_queries": 0,
        "new_screen_captures": 0,
        "ai_runs": 0,
        "game_runs": 0,
    }


def summarize(root: Path) -> dict[str, object]:
    counts: Counter = Counter()
    per_mode: dict[str, Counter] = {mode: Counter() for mode in MODES}
    try:
        if root.is_symlink() or not root.is_dir():
            return _result(counts, per_mode, "root_unavailable")
        folders = list(itertools.islice(root.iterdir(), ab.MAX_FOLDERS + 1))
        if len(folders) > ab.MAX_FOLDERS:
            return _result(counts, per_mode, "folder_limit_exceeded")
        for folder in folders:
            # Only existing writer's fixed folder pattern is accepted.
            if (not ab.NAME.fullmatch(folder.name) or folder.is_symlink() or
                    not folder.is_dir()):
                continue
            counts["runtime_folders"] += 1
            text = _read(root, folder)
            if text is None:
                counts["skipped_reports"] += 1
                continue
            counts["readable_reports"] += 1
            r = parse_record(text)
            mode_counts = per_mode[r.mode]
            mode_counts["readable"] += 1
            if r.mode == "unknown":
                counts["unknown_report_mode"] += 1

            for kind, status in (("cli", r.cli), ("screen", r.screen),
                                 ("triage", r.triage)):
                if status == "invalid":
                    counts["invalid_" + kind + "_annotation"] += 1

            # A tampered/malformed annotation invalidates cross-evidence
            # analysis of this record, but can still be counted as a format
            # problem. Never credit one valid block alongside a forged one.
            if "invalid" in (r.cli, r.screen, r.triage):
                counts["reports_with_invalid_annotations"] += 1
                continue

            cli = r.cli == "valid"
            screen = (
                r.screen == "valid" and
                r.screen_row is not None and
                MODE_AREAS.get(r.mode) == r.screen_row.area
            )
            if r.screen == "valid" and not screen:
                counts["screen_mode_mismatch"] += 1
            triage = (
                r.triage == "valid" and
                r.triage_area == MODE_AREAS.get(r.mode, "unknown")
            )
            if r.triage == "valid" and not triage:
                counts["triage_mode_mismatch"] += 1

            if cli:
                counts["with_cli_annotation"] += 1
                mode_counts["valid_cli"] += 1
                if any(outcome in ab.GENERIC and
                       outcome != "cli_response_accepted_not_game_event"
                       for _, outcome in r.cli_rows):
                    # A coarse hint, not a proven network root cause.
                    counts["cli_query_failure_hints"] += 1
            if screen:
                counts["with_screen_annotation"] += 1
                mode_counts["valid_screen"] += 1
            if triage:
                counts["with_triage_annotation"] += 1

            key = ("both_annotations" if cli and screen else
                   "cli_only" if cli else
                   "screen_only" if screen else "neither_annotation")
            counts[key] += 1
            if cli and not screen:
                pass
            elif screen and not cli:
                pass
            if cli and screen:
                mode_counts["both"] += 1
                # Both annotated != comparable: the only grounded CLI +
                # visual overlap is a gathering in-progress hint.
                assert r.screen_row is not None
                gathering_cli = (
                    r.mode == "채집" and
                    ("get_activity", "cli_gathering_in_progress") in r.cli_rows
                )
                claimed = (r.screen_row.comparison ==
                           "compatible_gathering_hints_only")
                if gathering_cli == claimed and gathering_cli:
                    counts["supported_gathering_comparison"] += 1
                    mode_counts["supported_comparison"] += 1
                elif gathering_cli != claimed:
                    # A document's own fixed labels disagree. This is NOT
                    # confirmation of a gameplay/CLI/screen state conflict.
                    counts["annotation_inconsistencies"] += 1
                    mode_counts["annotation_inconsistency"] += 1
                else:
                    counts["unsupported_semantic_comparison"] += 1
            elif screen and r.screen_row is not None and (
                r.screen_row.comparison == "compatible_gathering_hints_only"
            ):
                # A claim of a matched CLI observation without a CLI
                # annotation is uncorroborated offline, not a fake conflict.
                counts["unsupported_semantic_comparison"] += 1
        return _result(counts, per_mode, "offline_complete")
    except OSError:
        return _result(counts, per_mode, "io_unavailable")


def main() -> int:
    root = Path.home() / "MabiAutoAI" / "runtime-reports"
    print(json.dumps(summarize(root), ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
