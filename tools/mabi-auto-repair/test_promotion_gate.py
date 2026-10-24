"""Pure offline PR CI/gametest gate acceptance; GitHub never called."""
from __future__ import annotations

import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import promotion_gate as gate

PR = 98765
KEY = "a" * 24
BASE = "b" * 40
HEAD = "c" * 40
URL = f"https://github.com/{gate.REPO}/pull/{PR}"


def responses(*, conclusion="success", main_sha=BASE, added=None, draft=True,
              commit_parent=BASE, pr_state="open"):
    files = added if added is not None else [
        {"filename": "source/FishingAutomation/Fix.cs",
         "status": "modified", "changes": 4},
        {"filename": "tests/altering/Program.cs",
         "status": "modified", "changes": 5},
    ]
    return {
        f"repos/{gate.REPO}/pulls/{PR}": {
            "state": pr_state, "draft": draft, "merged_at": None, "changed_files": len(files),
            "head": {"ref": f"auto-repair/{KEY}-{BASE[:8]}", "sha": HEAD,
                     "repo": {"full_name": gate.REPO}},
            "base": {"ref": "main", "sha": BASE,
                     "repo": {"full_name": gate.REPO}},
            "body": f"- Incident fingerprint: \x60{KEY}\x60\n"
                    f"- Source SHA before repair: \x60{BASE}\x60\n",
        },
        f"repos/{gate.REPO}/branches/main": {
            "commit": {"sha": main_sha}
        },
        f"repos/{gate.REPO}/pulls/{PR}/commits?per_page=100": [{
            "sha": HEAD, "parents": [{"sha": commit_parent}]
        }],
        f"repos/{gate.REPO}/pulls/{PR}/files?per_page=100": files,
        f"repos/{gate.REPO}/actions/runs?head_sha={HEAD}&per_page=100": {
            "workflow_runs": [{
                "id": 778899, "head_sha": HEAD, "path": ".github/workflows/ci.yml",
                "name": "Check V3 Production Engine", "event": "pull_request",
                "status": "completed" if conclusion != "pending" else "in_progress",
                "conclusion": None if conclusion == "pending" else conclusion,
                "head_repository": {"full_name": gate.REPO},
                "run_attempt": 1, "created_at": "2026-10-24T14:00:00Z",
            }]
        },
    }


def report(root: Path, *, head=HEAD, rollback="passed", game="passed"):
    path = root / "tested-on-real-game.json"
    path.write_text(json.dumps({
        "schema": 1, "pr": PR, "tested_head_sha": head,
        "tested_by": "test_operator", "game_result": game,
        "rollback_result": rollback,
        "game_evidence": {
            "session_id": "1" * 24, "candidate_source_sha": head,
            "candidate_executable_sha256": "2" * 64,
            "stable_executable_sha256": "3" * 64,
            "candidate_proof_sha256": "4" * 64,
            "rollback_proof_sha256": "5" * 64,
            "new_runtime_errors": 0, "verification": "operator_attested",
        },
        "test_details": "Manual field recovery completed after entering processing menu.",
        "tested_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
    }), encoding="utf8")
    return path


class PromoteGateTests(unittest.TestCase):
    def audit_with(self, data):
        def getter(path):
            if path not in data:
                raise AssertionError("Unexpected GitHub API path: " + path)
            return data[path]
        return mock.patch.object(gate, "api", side_effect=getter)

    def test_ci_passed_is_waiting_on_game_not_auto_merge(self):
        with self.audit_with(responses()):
            result = gate.audit(PR)
        self.assertEqual(result["status"], "ci_passed_waiting_game")
        self.assertFalse(result["may_merge"])
        self.assertTrue(result["is_draft"])
        self.assertEqual(result["ci_run_id"], 778899)

    def test_ci_pending_or_failed_is_not_eligible(self):
        for conclusion, expected in (("pending", "ci_pending"),
                                     ("failure", "ci_failed"),
                                     ("cancelled", "ci_failed")):
            with self.subTest(conclusion=conclusion):
                with self.audit_with(responses(conclusion=conclusion)):
                    self.assertEqual(gate.audit(PR)["status"], expected)

    def test_base_updated_veto_prevents_stale_merge(self):
        with self.audit_with(responses(main_sha="d" * 40)):
            self.assertEqual(gate.audit(PR)["status"], "stale_main")

    def test_head_not_parent_of_base_veto(self):
        with self.audit_with(responses(commit_parent="d" * 40)):
            with self.assertRaisesRegex(gate.GateError, "commit chain"):
                gate.audit(PR)

    def test_bad_file_metadata_veto(self):
        for files in (
            [{"filename": "source/FishingAutomation/UpdateManager.cs", "status": "modified"},
             {"filename": "tests/altering/Program.cs", "status": "modified"}],
            [{"filename": "source/FishingAutomation/Fix.cs", "status": "modified"},
             {"filename": "tools/mabi-auto-repair/evil.py", "status": "added"}],
            [{"filename": "source/FishingAutomation/Fix.cs", "status": "modified"},
             {"filename": "tests/other/Test.cs", "status": "added"}],
            [{"filename": "source/FishingAutomation/Fix.cs", "status": "renamed"},
             {"filename": "tests/altering/Test.cs", "status": "added"}],
        ):
            with self.subTest(files=files):
                with self.audit_with(responses(added=files)):
                    with self.assertRaises(gate.GateError):
                        gate.audit(PR)

    def test_closed_pr_is_not_repromoted(self):
        with self.audit_with(responses(pr_state="closed")):
            self.assertEqual(gate.audit(PR)["status"], "pr_not_open")

    def test_no_merge_without_explicit_operator_ack(self):
        with tempfile.TemporaryDirectory() as temp:
            with mock.patch.object(gate, "audit",
                                   side_effect=AssertionError("Should not poll GitHub")), \
                 self.assertRaisesRegex(gate.GateError, "acknowledge-merge"):
                gate.promote(PR, Path(temp) / "absent.json", False)

    def test_game_signoff_must_match_exact_pr_head_rollback_and_utc(self):
        checked = {"pr": PR, "head_sha": HEAD}
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            for kw in ({"head": "d" * 40}, {"rollback": "pending"},
                       {"game": "pending"}):
                with self.subTest(kw=kw):
                    with self.assertRaises(gate.GateError):
                        gate.check_attestation(report(folder, **kw), checked)
            okay = gate.check_attestation(report(folder), checked)
            self.assertEqual(okay["game_test"], "operator_attested")
            data = json.loads(report(folder).read_text())
            data["tested_at_utc"] = "2020-01-01T00:00:00Z"
            p = folder / "old.json"
            p.write_text(json.dumps(data))
            with self.assertRaisesRegex(gate.GateError, "too old"):
                gate.check_attestation(p, checked)

    def test_game_evidence_required_and_bound_to_pr_head(self):
        with tempfile.TemporaryDirectory() as tmp:
            checked = {"pr": PR, "head_sha": HEAD}
            path = report(Path(tmp))
            data = json.loads(path.read_text())
            for mutated in (
                dict(data, game_evidence=None),
                dict(data, game_evidence=dict(data["game_evidence"],
                                              candidate_source_sha="d"*40)),
                dict(data, game_evidence=dict(data["game_evidence"],
                                              new_runtime_errors=1)),
                dict(data, game_evidence=dict(data["game_evidence"],
                                              candidate_proof_sha256="5"*64)),
                dict(data, game_evidence=dict(data["game_evidence"],
                                              stable_executable_sha256="2"*64)),
                dict(data, game_evidence=dict(data["game_evidence"],
                                              rollback_proof_sha256="invalid")),
            ):
                with self.subTest(evidence=mutated.get("game_evidence")):
                    path.write_text(json.dumps(mutated))
                    with self.assertRaises(gate.GateError):
                        gate.check_attestation(path, checked)

    def test_valid_explicit_operator_signoff_ready_merge_only_no_release(self):
        with tempfile.TemporaryDirectory() as tmp:
            commands = []
            attestation = report(Path(tmp))
            data = responses()
            def fake_gh(args, timeout=30):
                commands.append(args)
                return ""
            with self.audit_with(data), mock.patch.object(gate, "gh", side_effect=fake_gh):
                result = gate.promote(PR, attestation, True)
            self.assertEqual(result["status"], "merge_requested")
            self.assertIn("release", result)
            self.assertEqual(len(commands), 2)
            self.assertEqual(commands[0][:3], ["pr", "ready", str(PR)])
            self.assertEqual(commands[1][:3], ["pr", "merge", str(PR)])
            self.assertIn("--match-head-commit", commands[1])
            self.assertNotIn("--admin", commands[1])
            self.assertNotIn("--auto", commands[1])
            self.assertNotIn("release", " ".join(" ".join(args) for args in commands))

    def test_pr_changed_after_ready_blocks_merge(self):
        with tempfile.TemporaryDirectory() as tmp:
            attestation = report(Path(tmp))
            fresh = [
                {"pr": PR, "head_sha": HEAD, "status": "ci_passed_waiting_game",
                 "is_draft": True},
                {"pr": PR, "head_sha": "d" * 40, "status": "ci_passed_waiting_game",
                 "is_draft": False},
            ]
            with mock.patch.object(gate, "audit", side_effect=fresh), \
                 mock.patch.object(gate, "gh") as command:
                with self.assertRaisesRegex(gate.GateError, "changed"):
                    gate.promote(PR, attestation, True)
                self.assertEqual(command.call_count, 1)


if __name__ == "__main__":
    unittest.main()
