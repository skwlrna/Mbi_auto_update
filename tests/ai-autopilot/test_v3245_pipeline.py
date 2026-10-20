"""V3.2.45: duplicate Draft PR, safe edit paths and required regressions."""
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

AI_DIR = Path(__file__).resolve().parents[2] / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AI_DIR))
import autopilot
from draft_dedup import existing_draft_for_evidence
from github_pr import publish_verified_draft_pr, safe_candidate_path
from regression_policy import commands_for


class DraftReviewDedupTests(unittest.TestCase):
    SIG = "a" * 64
    EVENT = "b" * 16
    URL = "https://github.com/skwlrna/Mbi_auto_update/pull/251"

    def make_candidate(self, path: Path, *,
                       status="tests_passed_review_required",
                       pr_status="draft_created", url=None):
        directory = path / self.EVENT
        directory.mkdir(parents=True, exist_ok=True)
        (directory / "result.json").write_text(
            json.dumps({"event_id": self.EVENT, "status": status,
                        "pr": {"status": pr_status, "url": url or self.URL}}),
            encoding="utf-8")

    def entry(self, status="tests_passed_review_required", mtime=10_000.0):
        return [{"id": self.EVENT, "signature": self.SIG,
                 "status": status, "evidence_mtime": mtime}]

    def test_existing_valid_draft_blocks_identical_24h_candidate(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.make_candidate(root)
            prior = existing_draft_for_evidence(self.SIG, self.entry(), root,
                                                10_000.0 + 3610)
            self.assertEqual(prior, {"event_id": self.EVENT, "url": self.URL})
            self.assertIsNone(existing_draft_for_evidence(
                "c" * 64, self.entry(), root, 10_000.0 + 3610))
            self.assertIsNone(existing_draft_for_evidence(
                self.SIG, self.entry(), root, 10_000.0 + 86401))
            self.assertIsNone(existing_draft_for_evidence(
                self.SIG, self.entry(), root, 9_999.0))

    def test_failed_tests_unpublished_and_fake_links_never_suppress(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.make_candidate(root, status="tests_failed")
            self.assertIsNone(existing_draft_for_evidence(
                self.SIG, self.entry(), root, 11_000.0))
            self.make_candidate(root, pr_status="blocked")
            self.assertIsNone(existing_draft_for_evidence(
                self.SIG, self.entry(), root, 11_000.0))
            self.make_candidate(root, url="https://example.com/evil")
            self.assertIsNone(existing_draft_for_evidence(
                self.SIG, self.entry(), root, 11_000.0))
            self.make_candidate(root)
            self.assertIsNone(existing_draft_for_evidence(
                self.SIG, self.entry(status="tests_failed"), root, 11_000.0))

    def test_corrupt_oversize_symlink_and_invalid_ids_fail_closed(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / self.EVENT
            path.mkdir()
            (path / "result.json").write_text("{bad", encoding="utf-8")
            self.assertIsNone(existing_draft_for_evidence(
                self.SIG, self.entry(), root, 11_000.0))
            self.make_candidate(root)
            record = path / "result.json"
            copy = root / "external.json"
            copy.write_text(record.read_text(encoding="utf-8"), encoding="utf-8")
            record.unlink()
            try:
                record.symlink_to(copy)
            except (OSError, NotImplementedError):
                pass
            else:
                self.assertIsNone(existing_draft_for_evidence(
                    self.SIG, self.entry(), root, 11_000.0))
                record.unlink()
            record.write_text("x" * 132000, encoding="utf-8")
            self.assertIsNone(existing_draft_for_evidence(
                self.SIG, self.entry(), root, 11_000.0))
            self.assertIsNone(existing_draft_for_evidence(
                self.SIG, [{"id": "../" + self.EVENT, "signature": self.SIG,
                            "status": "tests_passed_review_required",
                            "evidence_mtime": 10_000.0}], root, 11_000.0))


class CriticalAutoFixGateTests(unittest.TestCase):
    def test_critical_scheduler_requires_own_real_regressions(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for kind in ("altering", "n02-resume", "combined-scheduler"):
                folder = root / "tests" / kind
                folder.mkdir(parents=True)
                (folder / "Regression.csproj").write_text("<Project />")
            source = "source/FishingAutomation/MultiAlteringCoordinator.cs"
            commands, error = commands_for(
                [source, "tests/altering/Program.cs"], root)
            self.assertEqual(commands, [])
            self.assertEqual(error,
                             "critical_source_regression_missing:tests/n02-resume/Program.cs")
            commands, error = commands_for(
                [source, "tests/altering/Program.cs",
                 "tests/n02-resume/Program.cs"], root)
            self.assertIsNone(error)
            self.assertEqual(len(commands), 2)
            self.assertTrue(all(cmd[0:2] == ["dotnet", "run"] for cmd in commands))
            source = "source/FishingAutomation/MultiAlteringDependencyScheduler.cs"
            commands, error = commands_for(
                [source, "tests/altering/Program.cs",
                 "tests/n02-resume/Program.cs"], root)
            self.assertEqual(error, "scheduler_regression_test_missing")
            commands, error = commands_for(
                [source, "tests/altering/Program.cs",
                 "tests/combined-scheduler/Program.cs"], root)
            self.assertIsNone(error)
            self.assertEqual(len(commands), 2)

    def test_ai_cannot_edit_its_host_updater_scripts_or_go_outside_repo(self):
        allowed = [
            "source/FishingAutomation/AlteringQueries.cs",
            "tests/altering/Program.cs", "changes/tests/stability/Program.cs",
        ]
        denied = [
            "source/FishingAutomation/ai-autopilot/autopilot.py",
            "source/FishingAutomation/ai-autopilot/github_pr.py",
            "source/FishingAutomation/UpdateManager.cs",
            "source/FishingAutomation/FishingAutomation.csproj",
            "source/tools/ApplyUpdate.ps1",
            "source/../.github/workflows/release.yml",
            "../secrets.txt", "/tmp/escape.cs",
            "source\\FishingAutomation\\Other.cs",
            "source/FishingAutomation/bin/a.cs",
            ".github/workflows/ci.yml",
        ]
        for name in allowed:
            self.assertTrue(autopilot.valid_edit(name, [
                "source/", "tests/", "changes/tests/"]), name)
            self.assertTrue(safe_candidate_path(name), name)
        for name in denied:
            self.assertFalse(autopilot.valid_edit(name, [
                "source/", "tests/", "changes/tests/"]) and
                safe_candidate_path(name), name)
            self.assertFalse(safe_candidate_path(name), name)

    def test_pr_publisher_refuses_unapproved_paths_before_any_git_or_network(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp)
            with patch("github_pr.user_opted_in", return_value=True), patch(
                    "github_pr.run", side_effect=AssertionError("must not execute")):
                outcome = publish_verified_draft_pr(
                    folder, "ai/autopilot-" + "a" * 16, "b" * 16,
                    ["source/FishingAutomation/ai-autopilot/autopilot.py"],
                    folder, [{"ok": True}])
            self.assertEqual(outcome["status"], "blocked")
            self.assertEqual(outcome["reason"], "changed_file_allowlist_failed")

if __name__ == "__main__":
    unittest.main()
