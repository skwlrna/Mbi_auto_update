"""Stage26: offline read-only release-handoff and rollback test suite.

All cases run on synthetic files or repository source only; no release action.
"""
from pathlib import Path
import json
import shutil
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
AI = ROOT / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AI))

import release_handoff_stage26 as handoff


class Stage26HandoffTests(unittest.TestCase):
    def assert_not_authorized(self, report):
        self.assertEqual(report["stage"], 26)
        self.assertEqual(report["schema"], 1)
        self.assertFalse(report["ready_for_production_release"])
        self.assertFalse(report["upstream_main_sha_checked"])
        self.assertFalse(report["same_head_ci_triple_success_checked"])
        self.assertFalse(report["user_pc_trial_verified"])
        self.assertFalse(report["user_approval_verified"])
        self.assertFalse(report["rollback_trial_verified"])
        self.assertFalse(report["build_commit_marker_provenance_verified"])
        self.assertFalse(report["upstream_divergence_resolved"])
        self.assertFalse(report["actual_release_version_checked"])
        for key in ("automatic_update_authorized", "auto_merge_authorized",
                    "auto_repair_authorized", "deployment_authorized",
                    "gameplay_authorized"):
            self.assertIs(report[key], False, key)
        for key in ("codex_executions", "network_requests",
                    "game_runs", "files_written"):
            self.assertEqual(report[key], 0)
        self.assertEqual(report["ai_accuracy_change"], "not_measured")

    def test_current_branch_is_dev_review_ready_but_never_release_authorized(self):
        report = handoff.audit(ROOT)
        self.assertEqual(report["status"],
                         "development_handoff_checks_passed_release_held")
        self.assertEqual(report["candidate_version"], "3.2.24")
        self.assertTrue(report["ready_for_upstream_reconciliation"])
        self.assertTrue(report["checks"]["stage25_integration_contract"])
        self.assertTrue(report["checks"]["local_version_fields_match"])
        self.assertTrue(report["checks"]["updater_matches_project_version"])
        self.assertTrue(report["checks"]["python_modules_packaging_declared"])
        self.assertTrue(report["checks"]["required_runtime_modules_present"])
        self.assertTrue(report["checks"]["pinned_source_marker_contract"])
        self.assertTrue(report["checks"]["rollback_scripts_and_ci_regression_present"])
        self.assertTrue(report["checks"]["historical_release_workflow_safety_contract"])
        self.assertFalse(report["checks"]["optional_local_build_output_matches_source"])
        self.assertEqual(report["module_count"],
                         len(handoff.REQUIRED_RUNTIME_MODULES))
        self.assertFalse(report["build_output_supplied"])
        self.assert_not_authorized(report)
        serialized = json.dumps(report)
        self.assertNotIn(str(ROOT), serialized)
        self.assertNotIn("PRIVATE_PERSON", serialized)
        self.assertNotIn("http", serialized)

    def test_matching_local_build_output_is_checked_without_executing_binary(self):
        with tempfile.TemporaryDirectory() as tmp:
            staging = Path(tmp) / "build"
            (staging / "ai-autopilot").mkdir(parents=True)
            for name in handoff.REQUIRED_RUNTIME_MODULES:
                shutil.copyfile(AI / name, staging / "ai-autopilot" / name)
            (staging / "FishingAutomation.exe").write_bytes(b"MZ\x00\x01offline-test")
            ok = handoff.audit(ROOT, staging)
            self.assertTrue(ok["build_output_supplied"])
            self.assertTrue(ok["checks"]["optional_local_build_output_matches_source"])
            self.assertTrue(ok["ready_for_upstream_reconciliation"])
            self.assert_not_authorized(ok)
            # A single replaced file blocks compatibility but not counted as
            # permission to repair or update any installed version.
            (staging / "ai-autopilot" / "incident_review_context.py").write_text(
                "this is not the checked in candidate", encoding="utf-8")
            bad = handoff.audit(ROOT, staging)
            self.assertEqual(bad["status"], "development_handoff_blocked")
            self.assertFalse(bad["checks"]["optional_local_build_output_matches_source"])
            self.assertFalse(bad["ready_for_upstream_reconciliation"])
            self.assert_not_authorized(bad)
            (staging / "ai-autopilot" / "incident_review_context.py").unlink()
            no = handoff.audit(ROOT, staging)
            self.assertFalse(no["checks"]["optional_local_build_output_matches_source"])
            self.assert_not_authorized(no)

    def test_missing_files_and_symlinks_block_without_public_paths(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "repo"
            root.mkdir()
            out = handoff.audit(root)
            self.assertEqual(out["status"], "development_handoff_blocked")
            self.assertFalse(out["ready_for_upstream_reconciliation"])
            self.assert_not_authorized(out)
            self.assertNotIn(str(root), json.dumps(out))
            link = Path(tmp) / "symlink"
            try:
                link.symlink_to(ROOT, target_is_directory=True)
            except (OSError, NotImplementedError):
                pass
            else:
                out2 = handoff.audit(link)
                self.assertEqual(out2["status"], "development_handoff_blocked")
                self.assert_not_authorized(out2)

    def test_version_or_rollback_mutation_blocks_auditor(self):
        original = handoff._bounded
        substitutions = {
            "source/FishingAutomation/UpdateManager.cs": [
                b'    public const string CurrentVersion = "V3.2.22";'],
            "source/tools/ApplyUpdate.ps1": [
                b"malformed rollback unsafe"],
            "source/FishingAutomation/FishingAutomation.csproj": [
                b"<Project><PropertyGroup><Version>3.2.24</Version>"
                b"<AssemblyVersion>3.2.22.0</AssemblyVersion>"
                b"<FileVersion>3.2.24.0</FileVersion></PropertyGroup></Project>"],
            ".github/workflows/release-v3224-auto-update.yml": [
                b"no provenance, no rollback script"],
        }
        for rel, replace in substitutions.items():
            def corrupt(root, candidate, bound=handoff.MAX_META_BYTES):
                if candidate == rel:
                    return replace[0]
                return original(root, candidate, bound)
            with self.subTest(mutation=rel), mock.patch.object(
                    handoff, "_bounded", side_effect=corrupt):
                result = handoff.audit(ROOT)
                self.assertEqual(result["status"], "development_handoff_blocked")
                self.assertFalse(result["ready_for_upstream_reconciliation"])
                self.assert_not_authorized(result)

    def test_no_enabling_releases_through_artificial_external_flags(self):
        # There is no external user-supplied boolean argument to bypass gates.
        self.assertNotIn("approve", handoff.audit.__code__.co_varnames)
        self.assertNotIn("deploy", handoff.audit.__code__.co_varnames)
        ok = handoff.audit(ROOT)
        self.assertNotIn("ready_for_release", ok)
        self.assertFalse(ok["ready_for_production_release"])
        self.assertEqual(ok["release_hold"],
                         "require_current_main_rebase_ci_and_live_game_user_approval")

    def test_module_only_reads_source_never_executes_tools_or_rewrites_files(self):
        import ast
        src = (AI / "release_handoff_stage26.py").read_text(encoding="utf-8")
        tree = ast.parse(src)
        imports = set()
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                imports.update(a.name.split(".")[0] for a in node.names)
            elif isinstance(node, ast.ImportFrom) and node.module:
                imports.add(node.module.split(".")[0])
        self.assertTrue(imports <= {
            "__future__", "argparse", "hashlib", "json", "pathlib", "re",
            "xml", "integration_safety_gate_stage25"
        })
        self.assertFalse(any(isinstance(n, ast.Call) and
                            getattr(n.func, "attr", "") in (
                                "write_text", "write_bytes", "unlink", "rename",
                                "subprocess", "run", "check_output", "system")
                             for n in ast.walk(tree)))
        self.assert_not_authorized(handoff.audit(ROOT))


if __name__ == "__main__":
    unittest.main()
