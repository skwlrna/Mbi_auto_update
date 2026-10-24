"""Offline acceptance for bounded, non-interactive, operator-signed game evidence."""
from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import game_validation as verify
import promotion_gate

PR = 654321
HEAD = "c" * 40
AUDIT = {"pr": PR, "head_sha": HEAD, "ci_run_id": 12345,
         "status": "ci_passed_waiting_game"}


class GameVerificationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.candidate = self.root / "candidate"
        self.candidate.mkdir()
        project = self.candidate / "source/FishingAutomation/FishingAutomation.csproj"
        project.parent.mkdir(parents=True)
        project.write_text("<Project/>", encoding="utf8")
        self.exe = self.candidate / "MabiAuto.exe"
        self.exe.write_bytes(b"candidate-build" * 40)
        self.stable = self.root / "installed" / "MabiAuto.exe"
        self.stable.parent.mkdir()
        self.stable.write_bytes(b"stable-release" * 40)
        self.logs = self.root / "logs"
        self.logs.mkdir()
        self.session = self.root / "session.json"
        self.report = self.root / "verified.json"
        self.sha = mock.patch.object(verify, "repo_head", return_value=HEAD)
        self.ci = mock.patch.object(verify.promotion_gate, "audit", return_value=dict(AUDIT))
        self.sha.start()
        self.ci.start()
        self.addCleanup(self.sha.stop)
        self.addCleanup(self.ci.stop)

    def start_session(self):
        return verify.start(PR, self.candidate, self.exe, self.stable,
                            self.logs, self.session)

    def make_proofs(self):
        candidate = self.root / "candidate-proof.png"
        rollback = self.root / "rollback-proof.png"
        candidate.write_bytes(b"\x89PNG\r\n\x1a\n" + b"candidate" * 30)
        rollback.write_bytes(b"\x89PNG\r\n\x1a\n" + b"rollback" * 30)
        return candidate, rollback

    def attest(self, **kwargs):
        cand, stable = self.make_proofs()
        opts = dict(session_file=self.session, candidate_proof=cand,
                    rollback_proof=stable, tester="local_tester",
                    details="Observed same game processing scene and verified progress "
                            "after original failure trigger; then restored stable macro.",
                    candidate_passed=True, rollback_passed=True,
                    acknowledged=True, output=self.report)
        opts.update(kwargs)
        return verify.attest(**opts)

    def test_start_binds_real_head_and_distinct_build(self):
        result = self.start_session()
        self.assertEqual(result["status"], "awaiting_game_test")
        saved = json.loads(self.session.read_text())
        self.assertEqual(saved["tested_head_sha"], HEAD)
        self.assertEqual(saved["game_result"], "not_verified")
        self.assertNotEqual(saved["candidate_sha256"], saved["stable_sha256"])

    def test_start_cannot_call_game_or_replace_installed_exe(self):
        before = self.stable.read_bytes()
        with mock.patch.object(verify.subprocess, "run",
                               side_effect=AssertionError("No game/process launch")):
            self.start_session()
        self.assertEqual(self.stable.read_bytes(), before)
        with self.assertRaisesRegex(verify.EvidenceError, "Existing session"):
            self.start_session()

    def test_start_rejects_unmatched_source_or_ci(self):
        with mock.patch.object(verify, "repo_head", return_value="b"*40):
            with self.assertRaisesRegex(verify.EvidenceError, "match"):
                self.start_session()
        with mock.patch.object(verify.promotion_gate, "audit",
                               return_value=dict(AUDIT, status="ci_pending")):
            with self.assertRaisesRegex(verify.EvidenceError, "CI must pass"):
                self.start_session()
        self.assertFalse(self.session.exists())

    def test_start_rejects_installed_binary_inside_candidate(self):
        with self.assertRaisesRegex(verify.EvidenceError, "outside isolated"):
            verify.start(PR, self.candidate, self.exe, self.exe,
                         self.logs, self.session)

    def test_no_new_error_is_not_automatic_game_success(self):
        self.start_session()
        result = verify.inspect(self.session)
        self.assertEqual(result["status"], "awaiting_human_game_evidence")
        self.assertEqual(result["new_runtime_errors"], 0)
        self.assertFalse(result["game_verified"])

    def test_new_runtime_error_blocks_attestation(self):
        self.start_session()
        path = self.logs / "runtime-errors/2026-10-24/new/summary.json"
        path.parent.mkdir(parents=True)
        path.write_text(json.dumps({"schema": 1, "source": "runtime"}))
        result = verify.inspect(self.session)
        self.assertEqual(result["status"], "blocked_new_runtime_error")
        with self.assertRaisesRegex(verify.EvidenceError, "New runtime errors"):
            self.attest()
        self.assertFalse(self.report.exists())

    def test_installed_stable_binary_mutation_blocks_rollback(self):
        self.start_session()
        self.stable.write_bytes(b"changed stable binary")
        with self.assertRaisesRegex(verify.EvidenceError, "rollback unsafe"):
            verify.inspect(self.session)

    def test_explicit_attestation_requires_both_real_results(self):
        self.start_session()
        with self.assertRaisesRegex(verify.EvidenceError, "Explicit real-game"):
            self.attest(acknowledged=False)
        with self.assertRaisesRegex(verify.EvidenceError, "Both candidate and rollback"):
            self.attest(rollback_passed=False)
        self.assertFalse(self.report.exists())

    def test_game_and_rollback_proofs_are_bound_to_current_head(self):
        self.start_session()
        result = self.attest()
        self.assertEqual(result["status"], "operator_attestation_recorded")
        saved = json.loads(self.report.read_text())
        checked = promotion_gate.check_attestation(
            self.report, {"pr": PR, "head_sha": HEAD})
        self.assertEqual(checked["game_test"], "operator_attested")
        self.assertEqual(saved["game_evidence"]["candidate_source_sha"], HEAD)
        self.assertNotEqual(saved["game_evidence"]["candidate_proof_sha256"],
                            saved["game_evidence"]["rollback_proof_sha256"])
        self.assertFalse(result["game_verified_automatically"])
        with self.assertRaisesRegex(verify.EvidenceError, "cannot be overwritten"):
            self.attest()

    def test_pr_head_change_blocks_report(self):
        self.start_session()
        with mock.patch.object(verify.promotion_gate, "audit",
                               return_value=dict(AUDIT, head_sha="d"*40)):
            with self.assertRaisesRegex(verify.EvidenceError, "changed"):
                self.attest()

    def test_preexisting_error_is_baseline_not_new_failure(self):
        file = self.logs / "runtime-errors/2026-10-24/old/summary.json"
        file.parent.mkdir(parents=True)
        file.write_text(json.dumps({"schema": 1, "source": "runtime"}))
        self.start_session()
        self.assertEqual(verify.inspect(self.session)["new_runtime_errors"], 0)


if __name__ == "__main__":
    unittest.main()
