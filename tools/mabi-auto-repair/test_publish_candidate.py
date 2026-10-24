"""Offline PR publishing tests; real temp Git push to LOCAL bare origin only.

GitHub CLI is mocked. No GitHub writes, Codex, live game or release actions.
"""
from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import publish_candidate as publisher
import repair


KEY = "e" * 24
SOURCE = "source/FishingAutomation/FixMe.cs"
TEST = "tests/altering/RegressionCheck.cs"


def git(args, root):
    result = subprocess.run(["git", *args], cwd=root, text=True,
                            capture_output=True, check=False, timeout=30)
    if result.returncode:
        raise AssertionError(f"local test Git failed: git {args[0]}")
    return result.stdout.strip()


class LocalCandidate:
    def __init__(self, root: Path):
        self.root = root
        self.app = root / "app"
        self.app.mkdir()
        self.bare = root / "offline-origin.git"
        git(["init", "--bare", str(self.bare)], root)
        git(["init", "-b", "main"], self.app)
        git(["config", "user.name", "Test Only"], self.app)
        git(["config", "user.email", "ci@example.invalid"], self.app)
        for name, content in (
            ("source/FishingAutomation/UpdateManager.cs",
             'public const string CurrentVersion = "V3.2.62";'),
            (SOURCE, "public class FixMe { }"),
            (TEST, "public class RegressionCheck { }"),
            ("tests/altering/Regression.csproj", "<Project></Project>"),
        ):
            path = self.app / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf8")
        git(["add", "."], self.app)
        git(["commit", "-m", "base"], self.app)
        self.head = git(["rev-parse", "HEAD"], self.app)
        git(["remote", "add", "origin", str(self.bare)], self.app)
        git(["push", "origin", "main"], self.app)
        self.state = repair.State(root / "state")
        self.workspace = self.state.folder / "workspaces" / KEY / "repo"
        self.workspace.parent.mkdir(parents=True)
        git(["worktree", "add", "--detach", str(self.workspace), self.head], self.app)
        for name, content in (
            (SOURCE, "public class FixMe { public bool Fixed => true; }"),
            (TEST, "public class RegressionCheck { public bool Pass => true; }"),
        ):
            (self.workspace / name).write_text(content, encoding="utf8")

    def analysis(self):
        return {
            "status": "candidate_passed",
            "candidate_verified": True,
            "source_version": "V3.2.62",
            "error_version": "V3.2.62",
            "candidate": {
                "status": "candidate_passed",
                "test_returncode": 0,
                "real_game_verified": False,
                "source_sha": self.head,
                "workspace": str(self.workspace),
                "changed_files": [SOURCE, TEST],
                "test": "tests/altering/Regression.csproj",
            },
        }

    def command(self, args, cwd, timeout=90):
        if args[:4] == ["git", "remote", "get-url", "origin"]:
            # Pretend allow-listed target, while all actual pushes remain
            # directed into the local bare test repository.
            return publisher.GIT_REMOTES[0]
        if args[:3] == ["gh", "pr", "create"]:
            self.gh_argv = args
            return "https://github.com/skwlrna/Mbi_auto_update/pull/54321"
        return self.original_command(args, cwd, timeout)

    def close(self):
        self.state.conn.close()


class PublishCandidateTests(unittest.TestCase):
    def test_offline_git_commit_push_and_draft_pr_with_no_runtime_evidence(self):
        with tempfile.TemporaryDirectory() as tmp:
            fixture = LocalCandidate(Path(tmp))
            try:
                fixture.original_command = publisher.command
                with mock.patch.object(publisher, "command", side_effect=fixture.command):
                    url = publisher.publish(fixture.state, KEY, fixture.app, fixture.analysis())
                self.assertEqual(url, "https://github.com/skwlrna/Mbi_auto_update/pull/54321")
                self.assertIn("--draft", fixture.gh_argv)
                self.assertEqual(fixture.gh_argv[fixture.gh_argv.index("--repo") + 1],
                                 publisher.REPO)
                branch = f"auto-repair/{KEY}-{fixture.head[:8]}"
                pushed = git(["--git-dir", str(fixture.bare), "rev-parse",
                              "refs/heads/" + branch], fixture.root)
                self.assertEqual(len(pushed), 40)
                staged = git(["--git-dir", str(fixture.bare), "diff-tree", "--no-commit-id",
                              "--name-only", "-r", pushed], fixture.root)
                self.assertEqual(set(staged.splitlines()), {SOURCE, TEST})
                # No raw incident text, PNG or logs ever sent to gh.
                body = fixture.gh_argv[fixture.gh_argv.index("--body") + 1]
                self.assertIn("not performed", body)
                self.assertNotIn("recent.log contents:", body)
            finally:
                fixture.close()

    def test_unverified_candidate_never_pushes(self):
        with tempfile.TemporaryDirectory() as tmp:
            fixture = LocalCandidate(Path(tmp))
            try:
                report = fixture.analysis()
                report["candidate_verified"] = False
                with self.assertRaisesRegex(publisher.PublishBlocked, "not verified"):
                    publisher.publish(fixture.state, KEY, fixture.app, report)
                self.assertNotIn("auto-repair/", git(["branch", "-a"], fixture.app))
            finally:
                fixture.close()

    def test_old_version_cannot_publish(self):
        with tempfile.TemporaryDirectory() as tmp:
            fixture = LocalCandidate(Path(tmp))
            try:
                report = fixture.analysis()
                report["error_version"] = "V3.2.61"
                with self.assertRaisesRegex(publisher.PublishBlocked, "differs"):
                    publisher.publish(fixture.state, KEY, fixture.app, report)
            finally:
                fixture.close()

    def test_model_cannot_publish_version_or_unregistered_file(self):
        with tempfile.TemporaryDirectory() as tmp:
            fixture = LocalCandidate(Path(tmp))
            try:
                report = fixture.analysis()
                report["candidate"]["changed_files"].append(
                    "source/FishingAutomation/UpdateManager.cs")
                with self.assertRaises(publisher.PublishBlocked):
                    publisher.publish(fixture.state, KEY, fixture.app, report)
                report = fixture.analysis()
                other = fixture.workspace / "tests" / "other" / "Inject.cs"
                other.parent.mkdir(parents=True)
                other.write_text("public class Inject {}", encoding="utf8")
                report["candidate"]["changed_files"].append("tests/other/Inject.cs")
                with self.assertRaises(publisher.PublishBlocked):
                    publisher.publish(fixture.state, KEY, fixture.app, report)
            finally:
                fixture.close()

    def test_origin_guard_refuses_local_or_unexpected_remote_without_push(self):
        with tempfile.TemporaryDirectory() as tmp:
            fixture = LocalCandidate(Path(tmp))
            try:
                with self.assertRaisesRegex(publisher.PublishBlocked, "Unexpected origin"):
                    publisher.publish(fixture.state, KEY, fixture.app, fixture.analysis())
                self.assertNotIn("auto-repair/", git(["branch", "-a"], fixture.app))
            finally:
                fixture.close()

    def test_remote_main_moved_refuses_publishing(self):
        with tempfile.TemporaryDirectory() as tmp:
            fixture = LocalCandidate(Path(tmp))
            try:
                # Real local remote advances after candidate's source SHA.
                git(["commit", "--allow-empty", "-m", "new upstream commit"], fixture.app)
                git(["push", "origin", "main"], fixture.app)
                fixture.original_command = publisher.command
                with mock.patch.object(publisher, "command", side_effect=fixture.command):
                    with self.assertRaisesRegex(publisher.PublishBlocked,
                                                "Original checkout changed"):
                        publisher.publish(fixture.state, KEY, fixture.app, fixture.analysis())
            finally:
                fixture.close()

    def test_candidate_different_than_tested_worktree_is_blocked(self):
        with tempfile.TemporaryDirectory() as tmp:
            fixture = LocalCandidate(Path(tmp))
            try:
                (fixture.workspace / "source/FishingAutomation/Extra.cs").write_text(
                    "public class Extra {}", encoding="utf8")
                with self.assertRaisesRegex(publisher.PublishBlocked, "differ from tested"):
                    publisher.publish(fixture.state, KEY, fixture.app, fixture.analysis())
            finally:
                fixture.close()

    def test_existing_branch_not_overwritten(self):
        with tempfile.TemporaryDirectory() as tmp:
            fixture = LocalCandidate(Path(tmp))
            try:
                branch = f"auto-repair/{KEY}-{fixture.head[:8]}"
                git(["branch", branch], fixture.app)
                git(["push", "origin", branch], fixture.app)
                fixture.original_command = publisher.command
                with mock.patch.object(publisher, "command", side_effect=fixture.command):
                    with self.assertRaisesRegex(publisher.PublishBlocked, "already exists"):
                        publisher.publish(fixture.state, KEY, fixture.app, fixture.analysis())
            finally:
                fixture.close()


if __name__ == "__main__":
    unittest.main()
