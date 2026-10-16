"""V3.2.19 AI workflow safety: source, test-stage, PR-CI separation."""
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
AI = ROOT / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AI))
import autopilot


class ReliablePipelineContract(unittest.TestCase):
    def test_dotnet_restore_fails_closed_before_codex_when_missing(self):
        with tempfile.TemporaryDirectory() as tmp:
            c = autopilot.config()
            with mock.patch.object(autopilot.shutil, "which", return_value=None), \
                 mock.patch.object(autopilot, "exec_codex") as codex:
                ok, why = autopilot.prepare_dotnet_environment(
                    Path(tmp), Path(tmp), c)
            self.assertFalse(ok)
            self.assertEqual(why, "dotnet_missing")
            codex.assert_not_called()

    def test_old_local_test_is_not_marked_as_final_pr_ci(self):
        agent = (AI / "autopilot.py").read_text(encoding="utf-8")
        dashboard = (ROOT / "source/FishingAutomation/AiAutopilotDashboard.cs").read_text(
            encoding="utf-8")
        self.assertIn('output["codex_internal_tests"] = "not_independently_verified"', agent)
        self.assertIn('output["host_offline_tests"] = {', agent)
        self.assertIn('"final_head_verified": False', agent)
        self.assertIn('"game_verified": False', agent)
        self.assertIn("PR 최종 커밋 CI·실게임은 별도 검증", dashboard)
        self.assertIn('string baseCommit = Field(obj, "base_commit", 40)', dashboard)
        self.assertIn('StringComparison.Ordinal', dashboard)
        self.assertNotIn("gh pr merge", agent)
        self.assertNotIn("automatic_deploy", dashboard)

    def test_release_source_guard_before_worktree_and_before_codex(self):
        agent = (AI / "autopilot.py").read_text(encoding="utf-8")
        self.assertLess(agent.index('require_pinned_source(c)'),
                        agent.index('workroot = expand(str(c["worktrees_dir"]))'))
        self.assertIn("release_source_mismatch_before_worktree", agent)
        self.assertIn("dotnet_restore_preflight_failed", agent)
        self.assertIn("release_source_changed_before_pr", agent)

    def test_preflight_never_auto_syncs_sources(self):
        agent = (AI / "autopilot.py").read_text(encoding="utf-8")
        self.assertLess(agent.index("if a.pipeline_check:"), agent.index("selected = prepare_source(c)"))
        self.assertIn("Only watch/once modes may prepare new source", agent)
