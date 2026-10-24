"""Offline evidence-to-repair tests. No actual Codex login, game access or PRs."""
from __future__ import annotations

import hashlib
import io
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import image_probe
import pipeline
import repair
from test_image_probe import png
from test_worktree_integration import make_repo, BAD, FIX, AFTER, git


INCIDENT_PATH = "runtime-errors/2026-10-24/183512_multi_altering/summary.json"


def incident(version="V3.2.62"):
    return dict(schema=1, source="runtime", title="다중가공 오류",
                detail="HUD 2/4 미니맵 인증이 실패해 입력 없이 정지", mode="가공",
                version=version)


def log_fixture(root: Path, version: str = "V3.2.62") -> tuple[str, bytes]:
    folder = root / INCIDENT_PATH
    folder.parent.mkdir(parents=True, exist_ok=True)
    folder.write_text(json.dumps(incident(version), ensure_ascii=False), encoding="utf-8")
    image = png()
    folder.with_name("screen.png").write_bytes(image)
    folder.with_name("recent.log").write_text(
        "19:00:00 알림: 관리 가공 K 실행 전 HUD 2/4\n"
        r"19:00:01 C:\Users\ownerA\Desktop\capture.png token=fixture-secret" "\n"
        "19:00:02 재진입 전 화면 인증 False; 입력 정지\n", encoding="utf-8")
    return "local:" + INCIDENT_PATH, image


def current_checkout(root: Path, version: str) -> Path:
    p = root / "repo/source/FishingAutomation/UpdateManager.cs"
    p.parent.mkdir(parents=True)
    p.write_text(f'public const string CurrentVersion = "{version}";', encoding="utf-8")
    return root / "repo"


class PipelineSafety(unittest.TestCase):
    def test_real_png_and_log_dry_run_no_ai_and_redacted_excerpt(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            origin, content = log_fixture(root / "logs", "V3.2.61")
            repo = current_checkout(root, "V3.2.62")
            with repair.State(root / "state") as state:
                key = state.add(origin, incident("V3.2.61"))
                with mock.patch.object(image_probe.subprocess, "run",
                                       side_effect=AssertionError("Unexpected Codex")):
                    result = pipeline.investigate(state, key, repo, root / "logs",
                                                  False, False)
                self.assertEqual(result["status"], "evidence_ready")
                self.assertEqual(result["image"]["sha256"], hashlib.sha256(content).hexdigest())
                self.assertEqual(result["image_calls"], 0)
                self.assertEqual(result["patch_calls_reserved"], 0)
                self.assertFalse(result["real_game_verified"])
                self.assertIn("source_version_not_reproduced", result["preflight_reasons"])
                self.assertIn("visual_evidence_missing", result["preflight_reasons"])
                self.assertEqual(state.job(key)["calls"], 0)
                recent = pipeline.recent_log(state.job(key), root / "logs")
                self.assertNotIn("ownerA", recent["excerpt"])
                self.assertNotIn("fixture-secret", recent["excerpt"])
                self.assertIn("HUD", recent["excerpt"])

    def test_refuse_execute_without_cost_ack_before_evidence_read(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            with repair.State(root / "state") as state:
                key = state.add("local:" + INCIDENT_PATH, incident())
                with mock.patch.object(pipeline, "recent_log",
                                       side_effect=AssertionError("Evidence read before consent")):
                    with self.assertRaisesRegex(ValueError, "acknowledge-cost"):
                        pipeline.investigate(state, key, root / "repo",
                                             root / "logs", True, False)
                self.assertEqual(state.job(key)["calls"], 0)

    def test_old_version_can_get_diagnostic_but_cannot_start_patch(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            origin, img = log_fixture(root / "logs", "V3.2.61")
            repo = current_checkout(root, "V3.2.62")
            with repair.State(root / "state") as state:
                key = state.add(origin, incident("V3.2.61"))
                received = []
                def simulated_analysis(*args, **kwargs):
                    received.append(kwargs["log_excerpt"])
                    return dict(model="gpt-6.1-sol", reasoning_effort="low",
                                image={"sha256": hashlib.sha256(img).hexdigest(), "bytes": len(img)},
                                codex_calls=1, status="image_analysis_returned",
                                diagnostic="화면 정보가 불충분해 필드 판정이 불확실합니다.")
                with mock.patch.object(pipeline, "inspect_image", side_effect=simulated_analysis), \
                     mock.patch.object(repair, "run_one",
                                       side_effect=AssertionError("Must not patch old versions")):
                    result = pipeline.investigate(state, key, repo, root / "logs", True, True)
                self.assertEqual(result["status"], "diagnosed_no_patch")
                self.assertIn("source_version_not_reproduced", result["reason"])
                self.assertEqual(result["image_calls"], 1)
                self.assertEqual(result["patch_calls_reserved"], 0)
                self.assertTrue(received and "HUD" in received[0])
                self.assertEqual(state.job(key)["calls"], 0)

    def test_failed_image_analysis_never_attempts_code_patch(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            origin, image = log_fixture(root / "logs")
            repo = current_checkout(root, "V3.2.62")
            with repair.State(root / "state") as state:
                key = state.add(origin, incident())
                def unsuccessful(*args, **kwargs):
                    return dict(model="gpt-6.1-sol", reasoning_effort="low",
                                image={"sha256": hashlib.sha256(image).hexdigest()},
                                codex_calls=1, status="image_analysis_failed")
                with mock.patch.object(pipeline, "inspect_image", side_effect=unsuccessful), \
                     mock.patch.object(repair, "run_one",
                                       side_effect=AssertionError("Must not patch")):
                    output = pipeline.investigate(state, key, repo, root / "logs", True, True)
                self.assertEqual(output["status"], "needs_review")
                self.assertEqual(output["reason"], "image_diagnostic_failed")
                self.assertEqual(state.job(key)["calls"], 0)

    def test_malicious_local_path_and_missing_log_fail_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            with self.assertRaisesRegex(ValueError, "Invalid runtime incident"):
                pipeline.recent_log({"source":"local:../../secrets.txt"}, root)
            origin, _ = log_fixture(root / "logs")
            file = root / "logs" / INCIDENT_PATH
            file.with_name("recent.log").unlink()
            with self.assertRaisesRegex(ValueError, "recent.log"):
                pipeline.recent_log({"source":origin}, root / "logs")

    def test_real_git_dotnet_flow_receives_image_diagnosis_and_redacted_log(self):
        """Real red/green C# build and worktree; both model invocations are mocked."""
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            origin, img = log_fixture(root / "logs")
            source = make_repo(root)
            before = repair.shell(["dotnet", "run", "--project",
                                   "tests/altering/Regression.csproj", "-c", "Release"],
                                  source, 150)
            self.assertNotEqual(before.returncode, 0, "Fixture must fail before patch")
            with repair.State(root / "state") as state:
                key = state.add(origin, incident())
                recorded = []
                real_shell = repair.shell
                real_which = repair.shutil.which

                def fake_which(name):
                    return "/fixture/codex" if name == "codex" else real_which(name)

                def fake_shell(argv, cwd, timeout=30, prompt=None):
                    if argv[0] != "codex":
                        return real_shell(argv, cwd, timeout, prompt)
                    recorded.append(prompt)
                    self.assertIn("visual evidence", prompt.lower()
                                  .replace("image diagnostic", "visual evidence"))
                    self.assertIn("HUD", prompt)
                    self.assertNotIn("fixture-secret", prompt)
                    (cwd / "source/FishingAutomation/RepairProbe.cs").write_text(FIX, encoding="utf-8")
                    (cwd / "tests/altering/Program.cs").write_text(AFTER, encoding="utf-8")
                    return subprocess.CompletedProcess(argv, 0, "fixed", "")

                def fake_image(job, local, github, execute, ack, log_excerpt=""):
                    self.assertTrue(execute and ack)
                    self.assertIn("HUD", log_excerpt)
                    self.assertNotIn("ownerA", log_excerpt)
                    self.assertEqual(local.read_bytes(), img)
                    return dict(model="gpt-6.1-sol", reasoning_effort="low",
                                image={"sha256": hashlib.sha256(img).hexdigest(), "bytes": len(img)},
                                codex_calls=1, status="image_analysis_returned",
                                diagnostic="HUD 2/4 화면 인증 실패; 코드를 살펴볼 후보")

                with mock.patch.object(pipeline, "inspect_image", side_effect=fake_image), \
                     mock.patch.object(repair, "shell", side_effect=fake_shell), \
                     mock.patch.object(repair.shutil, "which", side_effect=fake_which):
                    result = pipeline.investigate(state, key, source, root / "logs",
                                                  True, True)
                self.assertEqual(result["status"], "candidate_passed")
                self.assertTrue(result["candidate_verified"])
                self.assertEqual(result["image_calls"], 1)
                self.assertEqual(result["patch_calls_reserved"], 1)
                self.assertEqual(len(recorded), 1)
                self.assertEqual(state.job(key)["calls"], 1)
                self.assertEqual((source / "source/FishingAutomation/RepairProbe.cs")
                                 .read_text(encoding="utf-8"), BAD)
                self.assertEqual(git(source, "status", "--porcelain").stdout.strip(), "")
                with mock.patch.object(pipeline, "inspect_image",
                                       side_effect=AssertionError("Must not re-diagnose")):
                    with self.assertRaisesRegex(ValueError, "already had a repair"):
                        pipeline.investigate(state, key, source, root / "logs", True, True)


if __name__ == "__main__":
    unittest.main()
