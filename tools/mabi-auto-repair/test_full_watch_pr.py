"""Offline synthetic incident -> PNG/log -> mocked Codex -> .NET -> local Git push -> mock PR.

The GitHub remote is a LOCAL bare fixture. No real GitHub or game access.
"""
from __future__ import annotations

import hashlib
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import repair
import watch
import pipeline
import publish_candidate
from test_pipeline import log_fixture
from test_worktree_integration import make_repo, FIX, AFTER, git, BAD


@unittest.skipUnless(shutil.which("git") and shutil.which("dotnet"),
                     "Integration requires Git and .NET 8")
class SyntheticFullFlow(unittest.TestCase):
    def test_new_matching_incident_to_one_draft_pr_without_real_github(self):
        with tempfile.TemporaryDirectory(prefix="mabi-future-error-e2e-") as tmp:
            root = Path(tmp)
            source = make_repo(root)
            logs = root / "private-logs"
            logs.mkdir()
            bare = root / "LOCAL-only-origin.git"
            git(root, "init", "--bare", str(bare))
            git(source, "branch", "-M", "main")
            git(source, "remote", "add", "origin", str(bare))
            git(source, "push", "origin", "main")
            with repair.State(root / "state") as state:
                watch.initialize(state, logs)
                origin, image = log_fixture(logs, "V3.2.62")
                original_shell = repair.shell
                original_which = shutil.which
                original_publish = publish_candidate.command
                model_calls = []
                gh_calls = []

                def simulated_which(name):
                    if name in ("codex", "gh"):
                        return "/fixture/" + name
                    return original_which(name)

                def simulated_shell(argv, cwd, timeout=30, prompt=None):
                    if argv[0] != "codex":
                        return original_shell(argv, cwd, timeout, prompt)
                    model_calls.append(argv)
                    self.assertIn("HUD", prompt)
                    (cwd / "source/FishingAutomation/RepairProbe.cs").write_text(
                        FIX, encoding="utf-8")
                    (cwd / "tests/altering/Program.cs").write_text(
                        AFTER, encoding="utf-8")
                    return subprocess.CompletedProcess(argv, 0, "mock code patch", "")

                def simulated_image(job, path, is_github, execute, cost_ack,
                                    log_excerpt=""):
                    self.assertFalse(is_github)
                    self.assertEqual(path.read_bytes(), image)
                    self.assertIn("HUD", log_excerpt)
                    return {
                        "model": "gpt-6.1-sol", "reasoning_effort": "low",
                        "image": {"sha256": hashlib.sha256(image).hexdigest(),
                                  "bytes": len(image)},
                        "status": "image_analysis_returned",
                        "codex_calls": 1,
                        "diagnostic": "Synthetic HUD 2/4 field evidence uncertain",
                    }

                def simulated_publish(argv, cwd, timeout=90):
                    if argv[:4] == ["git", "remote", "get-url", "origin"]:
                        # Enforce real local pushes ONLY to fixture; avoid network.
                        return publish_candidate.GIT_REMOTES[0]
                    if argv[:3] == ["gh", "pr", "create"]:
                        gh_calls.append(argv)
                        return "https://github.com/skwlrna/Mbi_auto_update/pull/98765"
                    if argv == ["gh", "auth", "status"]:
                        return ""
                    return original_publish(argv, cwd, timeout)

                with mock.patch.object(repair, "shell", side_effect=simulated_shell), \
                     mock.patch.object(repair.shutil, "which", side_effect=simulated_which), \
                     mock.patch.object(pipeline, "inspect_image", side_effect=simulated_image), \
                     mock.patch.object(publish_candidate, "command", side_effect=simulated_publish):
                    first = watch.poll(state, logs, source, True, True, auto_pr=True)
                    second = watch.poll(state, logs, source, True, True, auto_pr=True)
                self.assertEqual(first["outcomes"], {"pr_opened": 1})
                self.assertEqual(first["new_events"][0]["source"], origin)
                self.assertEqual(first["new_events"][0]["pr_url"],
                                 "https://github.com/skwlrna/Mbi_auto_update/pull/98765")
                self.assertEqual(second["new_count"], 0)
                self.assertEqual(len(model_calls), 1)
                self.assertEqual(len(gh_calls), 1)
                self.assertIn("--draft", gh_calls[0])
                self.assertEqual(watch.report_status(state)["counts"]["pr_opened"], 1)
                self.assertEqual((source / "source/FishingAutomation/RepairProbe.cs")
                                 .read_text(encoding="utf-8"), BAD)
                self.assertEqual(git(source, "status", "--porcelain").stdout.strip(), "")
                remote_refs = git(root, "--git-dir", str(bare), "branch", "--list").stdout
                self.assertIn("auto-repair/", remote_refs)


if __name__ == "__main__":
    unittest.main()
