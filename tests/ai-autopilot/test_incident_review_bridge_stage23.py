"""Stage23: synthetic-only AI prompt bridge. Never launch Codex or game."""
import ast
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

BASE = Path(__file__).resolve().parents[2]
AI_DIR = BASE / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AI_DIR))

import autopilot
import incident_review_context as adapter
import incident_coverage_audit as audit
import incident_priority_review as priority
from test_incident_coverage_stage21 import cli, report, screen, write


class Stage23ContextTests(unittest.TestCase):
    def fixture(self, tmp):
        root = Path(tmp) / "runtime-reports"
        root.mkdir()
        folder = write(root, 1, report("채집", "PRIVATE_CHARACTER_ORIGINAL") +
                       cli("get_activity=cli_gathering_in_progress;age_ms=150") +
                       screen(comparison="no_comparable_cli_state"))
        write(root, 2, report("가공") +
              cli("get_altering_works=cli_altering_work_running;age_ms=40") +
              screen("altering", "altering_free_action_guard_passed"))
        return root, folder

    def test_opted_in_existing_prompt_gets_aggregate_fixed_labels_only(self):
        with tempfile.TemporaryDirectory() as tmp:
            root, folder = self.fixture(tmp)
            advice = adapter.for_runtime_error(folder, root)
            self.assertTrue(advice.startswith(adapter.BEGIN), advice)
            self.assertTrue(advice.endswith(adapter.END))
            self.assertIn("scope=global_aggregate_not_this_incident", advice)
            self.assertIn("review=P2:self_inconsistent_annotations:", advice)
            self.assertIn("review=P3:noncomparable_dual_hints:", advice)
            self.assertLessEqual(len(advice), adapter.MAX_ADVISORY)
            context = autopilot.context_for(folder)
            c = {"allowed_change_prefixes": ["source/FishingAutomation/"]}
            plain = autopilot.prompt_for(context, c)
            joined = autopilot.prompt_for(context, c, advice)
            self.assertEqual(joined.count("<untrusted-log>"), 1)
            self.assertEqual(joined.count("</untrusted-log>"), 1)
            self.assertIn(advice, joined)
            self.assertEqual(joined,
                             plain.replace("\n</untrusted-log>",
                                           "\n" + advice + "\n</untrusted-log>"))
            self.assertEqual(plain, autopilot.prompt_for(context, c, ""))
            self.assertIn("Never run git commit, git push, merge", joined)
            self.assertIn("Only edit code under these prefixes:", joined)
            self.assertIn("If the evidence is insufficient", joined)
            self.assertNotIn(advice, context,
                             "existing memory, hashes and context stay unchanged")
            advisory_text = json.dumps({"advice": advice})
            for forbidden in ("PRIVATE_CHARACTER", "20261018_221500",
                              str(root), "error.txt", "repair_authorized=true",
                              "execute_altering"):
                self.assertNotIn(forbidden, advisory_text)
            self.assertEqual(advice, adapter.for_runtime_error(folder, root))

    def test_root_and_event_path_must_match_exact_local_scope(self):
        with tempfile.TemporaryDirectory() as tmp:
            root, folder = self.fixture(tmp)
            external_root = Path(tmp) / "mcp"
            external_root.mkdir()
            external = external_root / folder.name
            external.mkdir()
            (external / "error.txt").write_text(report("채집"))
            self.assertEqual(adapter.for_runtime_error(external, root), "")
            self.assertEqual(adapter.for_runtime_error(folder, external_root), "")
            self.assertEqual(adapter.for_runtime_error(root / "not_found", root), "")
            self.assertEqual(adapter.for_runtime_error(folder, root / "missing"), "")
            other = root / "unapproved"
            other.mkdir()
            (other / "error.txt").write_text(report("채집"))
            self.assertEqual(adapter.for_runtime_error(other, root), "")
            root_symlink = Path(tmp) / "root_link"
            try:
                root_symlink.symlink_to(root, target_is_directory=True)
            except (OSError, NotImplementedError):
                pass
            else:
                self.assertEqual(adapter.for_runtime_error(folder, root_symlink), "")

    def test_invalid_or_missing_summary_never_enters_existing_ai_prompt(self):
        with tempfile.TemporaryDirectory() as tmp:
            root, folder = self.fixture(tmp)
            good = audit.summarize(root)
            for modified in (
                dict(good, gameplay_authorized=True),
                dict(good, stage=23),
                dict(good, counts={**good["counts"], "both_annotations": 999}),
                dict(good, diagnostic_accuracy_change="verified"),
                dict(good, by_mode={"PRIVATE": {}}),
            ):
                with self.subTest(modified=modified.get("stage")):
                    with mock.patch.object(audit, "summarize",
                                           return_value=modified):
                        self.assertEqual(adapter.for_runtime_error(folder, root), "")
            with mock.patch.object(audit, "summarize",
                                   side_effect=PermissionError("PRIVATE")):
                self.assertEqual(adapter.for_runtime_error(folder, root), "")

    def test_bounded_files_or_broken_data_fall_back_to_old_prompt(self):
        with tempfile.TemporaryDirectory() as tmp:
            root, folder = self.fixture(tmp)
            (folder / "error.txt").write_bytes(b"x" * (audit.ab.MAX_INPUT + 1))
            self.assertEqual(adapter.for_runtime_error(folder, root), "")
            (folder / "error.txt").write_bytes(b"error\x00binary")
            self.assertEqual(adapter.for_runtime_error(folder, root), "")
            (folder / "error.txt").unlink()
            self.assertEqual(adapter.for_runtime_error(folder, root), "")
            (folder / "error.txt").write_text(report("채집"))
            for i in range(audit.ab.MAX_FOLDERS + 1):
                (root / ("unexpected_%03d" % i)).mkdir()
            self.assertEqual(adapter.for_runtime_error(folder, root), "")
            self.assertEqual(autopilot.prompt_for("old context", {
                "allowed_change_prefixes": ["source/FishingAutomation/"]
            }), autopilot.prompt_for("old context", {
                "allowed_change_prefixes": ["source/FishingAutomation/"]
            }, ""))

    def test_no_queue_reorder_codex_relaunch_or_automatic_fix_from_advisory(self):
        tree = ast.parse((AI_DIR / "autopilot.py").read_text(encoding="utf-8"))
        fn = next(item for item in tree.body if
                  isinstance(item, ast.FunctionDef) and item.name == "process")
        source = ast.get_source_segment(
            (AI_DIR / "autopilot.py").read_text(encoding="utf-8"), fn)
        self.assertIsNotNone(source)
        self.assertIn("review_advisory = aggregate_review_for_error(", source)
        self.assertIn("prompt_for(context, c, review_advisory)", source)
        self.assertIn("revised_prompt = prompt_for(context, c, review_advisory)",
                      source)
        self.assertIn("memory_context = context", source)
        self.assertIn("record_attempt(", source)
        self.assertIn("require_pinned_source(c)", source)
        self.assertIn("require_isolated_ai_mode(c)", source)
        # No adapter in the scanner, dispatcher, or repair policy.
        self.assertEqual(
            [item.name for item in tree.body if isinstance(item, ast.FunctionDef)
             and "priority" in item.name], [])
        self.assertEqual(len(priority.POLICY), 10)
        self.assertEqual(adapter.TOP_ITEMS, 3)

    def test_top_three_policy_entries_are_bounded_even_with_many_findings(self):
        with tempfile.TemporaryDirectory() as tmp:
            root, folder = self.fixture(tmp)
            write(root, 3, report("던전") +
                  screen("abyss", "abyss_result_guard_passed"))
            write(root, 4, report("제작") +
                  screen("crafting", "crafting_result_popup_guard_passed")
                  .replace("repair_authorized=false", "repair_authorized=true"))
            write(root, 5, report("낚시"))
            text = adapter.for_runtime_error(folder, root)
            self.assertTrue(text)
            self.assertLessEqual(sum(row.startswith("review=")
                                     for row in text.splitlines()), 3)
            self.assertIn("review=P1:invalid_annotations:", text)
            self.assertIn("review=P1:mode_label_mismatch:", text)
            self.assertIn("review=P2:self_inconsistent_annotations:", text)


if __name__ == "__main__":
    unittest.main()
