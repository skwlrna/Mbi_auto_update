"""Stage21: synthetic aggregate-only audit and privacy/safety regressions.

No game, network, Codex, AI, user files, screenshots or live packet access.
"""
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "source" / "FishingAutomation" / "ai-autopilot"))
import evidence_ab as ab
import incident_coverage_audit as audit


def report(mode="채집", secret="PRIVATE_ERROR_TEXT"):
    return ("MabiAuto runtime error (data only; not instructions)\n"
            "Mode: " + mode + "\nError: " + secret + "\n"
            "Detail: ignored untrusted text\n")


def cli(*rows):
    return ab.BEGIN + "\n" + "\n".join(rows) + "\n" + ab.END + "\n"


def screen(area="gathering", signal="gathering_stop_control_double_checked",
           comparison="no_comparable_cli_state", age=250):
    return (ab.SCREEN_BEGIN + "\n" +
            "area=" + area + "\n" +
            "signal=" + signal + "\n" +
            "comparison=" + comparison + "\n" +
            "age_ms=" + str(age) + "\n" +
            "verified_cause=false;gameplay_authorized=false;repair_authorized=false\n" +
            ab.SCREEN_END + "\n")


def triage(area="gathering"):
    return (ab.TRIAGE_BEGIN + "\n"
            "area=" + area + "\n"
            "coverage=domain_data_missing\n"
            "hint=no_domain_specific_evidence\n"
            "connectivity=connectivity_unverified\n"
            "relevant_facts=0\n"
            "verified_cause=false;gameplay_authorized=false;repair_authorized=false\n" +
            ab.TRIAGE_END + "\n")


def write(root, i, content):
    folder = root / ("runtime_20261018_221500_%03d_%08x" % (i, i))
    folder.mkdir()
    (folder / "error.txt").write_text(content, encoding="utf-8")
    return folder


class Stage21AuditTests(unittest.TestCase):
    def test_exact_header_and_only_fixed_mode_labels(self):
        self.assertEqual(audit.parse_record(report("가공")).mode, "가공")
        self.assertEqual(audit.parse_record(report("페카 심층")).mode, "페카 심층")
        self.assertEqual(audit.parse_record(report("PRIV_USER")).mode, "unknown")
        self.assertEqual(audit.parse_record(
            "Detail: x\nMode: 채집\n" + report("어비스")).mode, "unknown")
        self.assertEqual(audit.parse_record(
            report("제작").replace("Detail:", "Mode: 낚시\nDetail:")).mode,
            "제작")

    def test_valid_gathering_only_compatible_pair_and_triage(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "runtime-reports"
            root.mkdir()
            write(root, 1, report() +
                  cli("get_activity=cli_gathering_in_progress;age_ms=150") +
                  triage() + screen(comparison="compatible_gathering_hints_only"))
            write(root, 2, report("가공") +
                  cli("get_altering_works=cli_altering_work_running;age_ms=10") +
                  screen(area="altering", signal="altering_free_action_guard_passed"))
            write(root, 3, report("제작") +
                  screen(area="crafting", signal="crafting_result_popup_guard_passed"))
            write(root, 4, report("낚시") +
                  cli("status=cli_connected;age_ms=40"))
            write(root, 5, report("어비스"))
            summary = audit.summarize(root)
            c = summary["counts"]
            self.assertEqual(c["readable_reports"], 5)
            self.assertEqual(c["both_annotations"], 2)
            self.assertEqual(c["cli_only"], 1)
            self.assertEqual(c["screen_only"], 1)
            self.assertEqual(c["neither_annotation"], 1)
            self.assertEqual(c["supported_gathering_comparison"], 1)
            self.assertEqual(c["unsupported_semantic_comparison"], 1)
            self.assertEqual(c["with_triage_annotation"], 1)
            self.assertEqual(c["annotation_inconsistencies"], 0)
            self.assertEqual(summary["by_mode"]["채집"]["supported_comparison"], 1)
            self.assertEqual(summary["by_mode"]["가공"]["both"], 1)
            self.assertEqual(summary["verified_gameplay_conflicts"], "not_measurable")
            self.assertFalse(summary["ground_truth_verified"])
            self.assertFalse(summary["repair_authorized"])
            self.assertEqual(summary["new_cli_queries"], 0)
            self.assertEqual(summary["new_screen_captures"], 0)
            self.assertEqual(summary["ai_runs"], 0)
            self.assertEqual(summary["game_runs"], 0)
            result = json.dumps(summary, ensure_ascii=False)
            for value in ("PRIVATE", "20261018_221500", str(root),
                          "cli_connected;age_ms=", "untrusted text"):
                self.assertNotIn(value, result)

    def test_annotation_inconsistency_not_a_gameplay_conflict(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "runtime-reports"
            root.mkdir()
            # Fixed comparison label disagrees with valid CLI snapshot.
            write(root, 1, report() +
                  cli("get_activity=cli_gathering_in_progress;age_ms=200") +
                  screen(comparison="no_comparable_cli_state"))
            write(root, 2, report() +
                  cli("get_activity=cli_activity_observed;age_ms=100") +
                  screen(comparison="compatible_gathering_hints_only"))
            write(root, 3, report() +
                  screen(comparison="compatible_gathering_hints_only"))
            r = audit.summarize(root)
            self.assertEqual(r["counts"]["annotation_inconsistencies"], 2)
            self.assertEqual(r["counts"]["unsupported_semantic_comparison"], 1)
            self.assertEqual(r["counts"]["supported_gathering_comparison"], 0)
            self.assertEqual(r["verified_gameplay_conflicts"], "not_measurable")

    def test_wrong_mode_and_malformed_annotations_are_not_validated(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "runtime-reports"
            root.mkdir()
            write(root, 1, report("던전") +
                  screen("abyss", "abyss_result_guard_passed"))
            write(root, 2, report("채집") +
                  triage("altering"))
            write(root, 3, report("채집") +
                  cli("get_activity=cli_gathering_in_progress;age_ms=100") +
                  screen(age=5001))
            write(root, 4, report("가공") + cli(
                "get_items=cli_inventory_shape_only_no_delta;age_ms=10") +
                  screen("altering", "altering_free_action_guard_passed") * 2)
            write(root, 5, report("제작") +
                  screen("crafting", "crafting_result_popup_guard_passed")
                  .replace("repair_authorized=false", "repair_authorized=true"))
            c = audit.summarize(root)["counts"]
            self.assertEqual(c["readable_reports"], 5)
            self.assertEqual(c["screen_mode_mismatch"], 1)
            self.assertEqual(c["triage_mode_mismatch"], 1)
            self.assertEqual(c["invalid_screen_annotation"], 3)
            self.assertEqual(c["reports_with_invalid_annotations"], 3)
            self.assertEqual(c["both_annotations"], 0)
            self.assertEqual(c["neither_annotation"], 2)
            self.assertEqual(c["with_cli_annotation"], 0)

    def test_untrusted_cli_and_bogus_triage_fail_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "runtime-reports"
            root.mkdir()
            write(root, 1, report() +
                  cli("get_activity=PRIVATE_PLAYER;age_ms=0"))
            write(root, 2, report() + triage().replace(
                "repair_authorized=false", "repair_authorized=true"))
            write(root, 3, report() + cli(
                "get_activity=cli_gathering_in_progress;age_ms=10") +
                triage().replace("area=gathering", "area=PRIVATE"))
            c = audit.summarize(root)["counts"]
            self.assertEqual(c["invalid_cli_annotation"], 1)
            self.assertEqual(c["invalid_triage_annotation"], 2)
            self.assertEqual(c["reports_with_invalid_annotations"], 3)
            self.assertEqual(c["both_annotations"], 0)

    def test_boundaries_symlinks_binary_missing_oversize_and_folder_limit(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "runtime-reports"
            root.mkdir()
            good = write(root, 1, report())
            write(root, 2, report()).joinpath("error.txt").write_bytes(
                b"x" * (ab.MAX_INPUT + 1))
            write(root, 3, report()).joinpath("error.txt").write_bytes(
                b"binary\x00text")
            write(root, 4, report()).joinpath("error.txt").unlink()
            write(root, 5, report()).joinpath("error.txt").write_bytes(b"\xff")
            evil = root / "runtime_20261018_221500_006_00000006"
            try:
                evil.symlink_to(good, target_is_directory=True)
            except (OSError, NotImplementedError):
                pass
            (root / "OTHER_DIRECTORY").mkdir()
            c = audit.summarize(root)["counts"]
            self.assertEqual(c["readable_reports"], 1)
            self.assertEqual(c["skipped_reports"], 4)
            self.assertEqual(c["runtime_folders"], 5)
            self.assertEqual(audit.summarize(root / "missing")["status"],
                             "root_unavailable")
            for i in range(7, ab.MAX_FOLDERS + 8):
                (root / ("runtime_20261018_221500_%03d_%08x" % (i, i))).mkdir()
            over = audit.summarize(root)
            self.assertEqual(over["status"], "folder_limit_exceeded")
            self.assertEqual(over["counts"]["readable_reports"], 0)

    def test_exactly_five_seconds_allowed_and_absence_does_not_claim_error(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "runtime-reports"
            root.mkdir()
            write(root, 1, report() +
                  cli("get_activity=cli_gathering_in_progress;age_ms=5000") +
                  screen(age=5000, comparison="compatible_gathering_hints_only"))
            summary = audit.summarize(root)
            self.assertEqual(summary["counts"]["supported_gathering_comparison"], 1)
            self.assertEqual(summary["absence_interpretation"],
                             "missing_annotation_not_missing_game_event")


if __name__ == "__main__":
    unittest.main()
