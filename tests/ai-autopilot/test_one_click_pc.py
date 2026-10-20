"""Offline one-click smoke: pinned source, disposable archive, no game input."""
import io
import json
import os
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch

LIB = Path(__file__).resolve().parents[2] / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(LIB))
import one_click_test as check


class OneClickPcTests(unittest.TestCase):
    def test_archive_refuses_path_escape_and_symlink(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for path, kind in [("../evil.cs", "file"),
                               ("a/b.cs", "symlink"),
                               ("/outside.cs", "file"),
                               ("foo\\bar.cs", "file")]:
                archive = root / "input.tar"
                with tarfile.open(archive, "w") as tar:
                    member = tarfile.TarInfo(path)
                    if kind == "symlink":
                        member.type = tarfile.SYMTYPE
                        member.linkname = "target.cs"
                        tar.addfile(member)
                    else:
                        data = b"Hello"
                        member.size = len(data)
                        tar.addfile(member, io.BytesIO(data))
                with self.assertRaises(ValueError):
                    check.safe_extract_archive(archive, root / "out")

    def test_offline_run_five_guards_and_stores_local_json(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            home = root / "home"
            home.mkdir()
            installed_marker = root / "BUILD_COMMIT.txt"
            installed_marker.write_text("a" * 40, encoding="ascii")
            source = home / "MabiAutoAI" / "repo"
            source.mkdir(parents=True)
            results = root / "local-results"
            commands = []

            def fake_archive(args, **kwargs):
                archive = Path(args[args.index("--output") + 1])
                with tarfile.open(archive, "w") as tar:
                    name = "source/FishingAutomation/FishingAutomation.csproj"
                    member = tarfile.TarInfo(name)
                    payload = b"<Project />"
                    member.size = len(payload)
                    tar.addfile(member, io.BytesIO(payload))
                class Done:
                    returncode = 0
                return Done()

            def fake_step(argv, cwd, limit):
                self.assertTrue(str(cwd).startswith(str(Path(directory))))
                self.assertTrue((cwd / "source/FishingAutomation/FishingAutomation.csproj").exists())
                commands.append(tuple(argv))
                return "passed", "exit_0"

            with patch.object(check, "release_snapshot",
                              return_value=(source, "verified_local_pinned_source")), patch.object(
                     check, "check_checkout", return_value=(True, "ok")), patch.object(
                     check.subprocess, "run", side_effect=fake_archive), patch.object(
                     check, "check_step", side_effect=fake_step):
                report = check.run_once(
                    home, results, marker=installed_marker, locate=lambda exe: "installed")
            self.assertEqual(report["result"], "passed_offline")
            self.assertEqual(len(commands), 5)
            self.assertTrue(all(r["status"] == "passed" for r in report["tests"]))
            for flag in ("gameTested", "gameInputSent", "codexExecuted",
                         "prCreated", "autoUpdatePerformed"):
                self.assertFalse(report[flag])
            self.assertEqual(json.loads(
                (results / "latest-result.json").read_text(encoding="utf-8"))["result"],
                "passed_offline")

    def test_missing_sdk_or_pinned_source_blocks_without_running_tests(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            marker = root / "BUILD_COMMIT.txt"
            marker.write_text("b" * 40, encoding="ascii")
            with patch.object(check, "check_step", side_effect=AssertionError(
                    "must not execute any test")), patch.object(
                    check, "release_snapshot", return_value=(None, "missing")):
                missing_sdk = check.run_once(root, root / "report1",
                    marker=marker, locate=lambda exe: None)
                missing_source = check.run_once(root, root / "report2",
                    marker=marker, locate=lambda exe: "/bin/exe")
            self.assertEqual(missing_sdk["result"], "blocked_environment")
            self.assertEqual(missing_source["result"], "blocked_environment")
            self.assertEqual(missing_source["reason"], "missing")
            self.assertFalse(missing_source["gameTested"])

    def test_first_failure_does_not_run_later_steps_or_call_codex(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            marker = root / "BUILD_COMMIT.txt"
            marker.write_text("c" * 40, encoding="ascii")
            source = root / "repo"
            source.mkdir()
            calls = []

            def fake_archive(args, **kwargs):
                archive = Path(args[args.index("--output") + 1])
                with tarfile.open(archive, "w") as tar:
                    payload = b"<Project />"
                    member = tarfile.TarInfo("source/FishingAutomation/FishingAutomation.csproj")
                    member.size = len(payload)
                    tar.addfile(member, io.BytesIO(payload))
                class Done:
                    returncode = 0
                return Done()

            def fail_step(argv, cwd, timeout):
                calls.append(argv)
                return "failed", "test_nonzero_exit"

            with patch.object(check, "release_snapshot",
                              return_value=(source, "verified")), patch.object(
                     check, "check_checkout", return_value=(True, "ok")), patch.object(
                     check.subprocess, "run", side_effect=fake_archive), patch.object(
                     check, "check_step", side_effect=fail_step):
                output = check.run_once(root, root / "report", marker=marker,
                    locate=lambda exe: "installed")
            self.assertEqual(output["result"], "tests_failed")
            self.assertEqual(len(calls), 1)
            self.assertFalse(output["prCreated"])

    def test_ui_wiring_uses_idle_guard_and_report_only(self):
        ui = (LIB.parent / "MainForm.AiAutopilot.cs").read_text(encoding="utf-8")
        host = (LIB.parent / "AiOneClickTestHost.cs").read_text(encoding="utf-8")
        self.assertIn("PC 원클릭 안전 테스트", ui)
        self.assertIn("if (AnyRunning || _activeMode is not null)", ui)
        self.assertIn("RunAiOneClickTestAsync", ui)
        self.assertIn("_aiOneClickTestCancellation?.Cancel();", ui)
        self.assertIn("UseShellExecute = false", host)
        self.assertIn("one_click_test.py", host)
        self.assertIn("gameInputSent", host)
        for forbidden in ("StartSelected()", "SendInput(", "publish_verified_draft_pr(",
                          "UpdateManager.CheckForUpdates"):
            self.assertNotIn(forbidden, host)


if __name__ == "__main__":
    unittest.main()
