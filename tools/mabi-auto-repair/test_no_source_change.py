"""No-source-change bounded AI repair paths; mocked, zero paid calls."""
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import TestCase, mock
import subprocess

import repair


DETAIL = "시설 카드 원래 좌표 미반응, 고유 OCR 대체 좌표 미검증"
HEAD = "a" * 40


def setup_case(root: Path):
    repo = root / "repo"
    base = repo / "source/FishingAutomation"
    (base / "dungeon").mkdir(parents=True)
    (repo / "tests/altering").mkdir(parents=True)
    (base / "FishingAutomation.csproj").write_text("<Project/>", encoding="utf-8")
    (base / "UpdateManager.cs").write_text(
        'public const string CurrentVersion = "V3.2.62";', encoding="utf-8")
    (base / "dungeon/AlteringScreen.cs").write_text("class AlteringScreen {}")
    (base / "AlteringTravelActivityPolicy.cs").write_text("class Travel {}")
    (repo / "tests/altering/Program.cs").write_text("class Regression {}")
    state = repair.State(root / "state")
    key = state.add("mock:2026-10-25", {
        "schema": 1, "source": "runtime", "title": "다중가공 오류",
        "detail": DETAIL, "version": "V3.2.62", "mode": "가공"
    })
    return repo, state, key


def fake_shell_factory(diffs, prompts):
    attempt = {"count": 0}
    def fake(command, cwd, timeout=30, prompt=None):
        if command[:2] == ["git", "rev-parse"]:
            return subprocess.CompletedProcess(command, 0, HEAD + "\n", "")
        if command[:2] == ["git", "status"]:
            in_worktree = "workspaces" in str(cwd)
            diff = diffs[min(max(attempt["count"] - 1, 0), len(diffs)-1)] if in_worktree else ""
            return subprocess.CompletedProcess(command, 0, diff, "")
        if command[:3] == ["git", "worktree", "add"]:
            worktree = Path(command[4])
            (worktree / "source/FishingAutomation/dungeon").mkdir(parents=True)
            (worktree / "tests/altering").mkdir(parents=True)
            (worktree / "source/FishingAutomation/dungeon/AlteringScreen.cs").write_text("class A {}")
            (worktree / "source/FishingAutomation/AlteringTravelActivityPolicy.cs").write_text("class T {}")
            (worktree / "tests/altering/Program.cs").write_text("class R {}")
            return subprocess.CompletedProcess(command, 0, "", "")
        if command[0] == "codex":
            attempt["count"] += 1
            prompts.append(prompt)
            return subprocess.CompletedProcess(
                command, 0, "No evidence. C:\\Users\\private\\Desktop\\secret.png token=private-token", "")
        if command[0] == "dotnet":
            return subprocess.CompletedProcess(command, 0, "All tests passed", "")
        raise AssertionError(command)
    return fake


class NoSourceChangeTests(TestCase):
    def test_redacted_log_preserves_multiple_lines_and_full_limit(self):
        timeline = "\n".join(f"[08:10:{i:02d}] Activity Dialogue=True event {i} " +
                             ("z" * 90) for i in range(60))
        timeline += "\nC:\\Users\\sensitive-user\\Desktop\\trace.png token=private-value"
        excerpt = repair.sanitized_log(timeline, 5000)
        self.assertGreater(len(excerpt), 2000)
        self.assertLessEqual(len(excerpt), 5000)
        self.assertGreater(excerpt.count("\n"), 15)
        self.assertIn("Dialogue=True", excerpt)
        self.assertNotIn("sensitive-user", excerpt)
        self.assertNotIn("private-value", excerpt)

    def test_source_hints_use_bounded_existing_files_only(self):
        with TemporaryDirectory() as temp:
            repo, state, key = setup_case(Path(temp))
            with state:
                job = state.job(key)
                hints = repair.source_hints(job, {
                    "log_excerpt": "Dialogue=True. 관리 가공 입력 차단, activity=이동 중"
                }, repo)
                self.assertTrue(any("AlteringScreen.cs" in h for h in hints))
                self.assertTrue(any("AlteringTravelActivityPolicy.cs" in h for h in hints))
                self.assertTrue(any("tests/altering/Program.cs" in h for h in hints))
                self.assertFalse(any("secrets" in h for h in hints))
                self.assertEqual(repair.source_hints(
                    {"mode": "채집", "detail": "시설 카드"}, None, repo), [])

    def test_default_no_change_records_safe_diagnostic_without_retry(self):
        with TemporaryDirectory() as temp:
            repo, state, key = setup_case(Path(temp))
            with state:
                prompts = []
                runner = fake_shell_factory([""], prompts)
                with mock.patch.object(repair.shutil, "which", return_value="/mock/tool"), \
                     mock.patch.object(repair, "shell", side_effect=runner):
                    out = repair.run_one(state, key, repo, True, True, None)
                self.assertEqual(out["status"], "needs_review")
                self.assertEqual(out["reason"], "No C# source change")
                self.assertEqual(out["patch_attempts"], 1)
                self.assertEqual(out["codex_calls"], 1)
                self.assertEqual(len(prompts), 1)
                self.assertIn("AlteringScreen.cs", prompts[0])
                self.assertIn("registered altering regression tests", prompts[0])
                self.assertNotIn("private-token", str(out))
                self.assertNotIn("private", out["no_change_diagnostic"]["stdout_excerpt_untrusted"])
                self.assertEqual(state.job(key)["calls"], 1)
                with self.assertRaisesRegex(ValueError, "repeated"):
                    state.reserve(key, None)

    def test_explicit_opt_in_once_retries_no_change_then_valid_test(self):
        with TemporaryDirectory() as temp:
            repo, state, key = setup_case(Path(temp))
            with state:
                prompts = []
                runner = fake_shell_factory([
                    "",
                    " M source/FishingAutomation/dungeon/AlteringScreen.cs\n"
                    " M tests/altering/Program.cs\n",
                ], prompts)
                with mock.patch.object(repair.shutil, "which", return_value="/mock/tool"), \
                     mock.patch.object(repair, "shell", side_effect=runner):
                    out = repair.run_one(state, key, repo, True, True, None,
                                         retry_regression_once=True)
                self.assertEqual(out["status"], "candidate_passed")
                self.assertEqual(out["patch_attempts"], 2)
                self.assertEqual(out["codex_calls"], 2)
                self.assertEqual(len(prompts), 2)
                self.assertIn("ONE final correction", prompts[1])
                self.assertEqual(state.job(key)["calls"], 1)
                self.assertEqual(state.job(key)["status"], "candidate_passed")

    def test_opted_in_no_change_twice_stops_without_third_call(self):
        with TemporaryDirectory() as temp:
            repo, state, key = setup_case(Path(temp))
            with state:
                prompts = []
                runner = fake_shell_factory(["", ""], prompts)
                with mock.patch.object(repair.shutil, "which", return_value="/mock/tool"), \
                     mock.patch.object(repair, "shell", side_effect=runner):
                    out = repair.run_one(state, key, repo, True, True, None,
                                         retry_regression_once=True)
                self.assertEqual(out["status"], "needs_review")
                self.assertEqual(out["patch_attempts"], 2)
                self.assertEqual(len(prompts), 2)
                self.assertEqual(state.job(key)["calls"], 1)
                self.assertTrue((Path(temp) / "state/workspaces" / key / "result.json").exists())


if __name__ == "__main__":
    import unittest
    unittest.main()
