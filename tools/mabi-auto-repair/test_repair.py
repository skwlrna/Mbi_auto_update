"""MabiAutoRepair offline tests: no real Codex, network, Windows game or GitHub writes."""
import json
import subprocess
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import TestCase, main, mock

import repair


def incident(detail="시설 UI 미확인, 안전 정지"):
    return {"schema": 1, "source": "runtime", "version": "V3.2.60",
            "mode": "가공", "title": "다중가공 오류", "detail": detail,
            "timeUtc": "2026-10-24T06:32:53Z", "processId": 23316}


class OfflineTests(TestCase):
    def test_deduplicate_reimport_without_extra_count(self):
        with TemporaryDirectory() as tmp:
            with repair.State(Path(tmp)) as state:
                key = state.add("github:one", incident())
                self.assertEqual(state.add("github:one", incident()), key)
                self.assertEqual(state.add("github:two", incident()), key)
                self.assertEqual(state.job(key)["hits"], 2)
                self.assertEqual(len(state.jobs()), 1)
                self.assertEqual(state.job(key)["calls"], 0)

    def test_paths_and_secrets_redacted_before_storage(self):
        with TemporaryDirectory() as tmp:
            with repair.State(Path(tmp)) as state:
                key = state.add("sample", incident(r"C:\Users\private-user\Desktop\a.png token=secret-value"))
                self.assertNotIn("private-user", state.job(key)["detail"])
                self.assertNotIn("secret-value", state.job(key)["detail"])
                self.assertIn("<path>", state.job(key)["detail"])

    def test_non_runtime_record_rejected(self):
        with TemporaryDirectory() as tmp:
            with repair.State(Path(tmp)) as state:
                row = incident()
                row["source"] = "visual-test"
                self.assertIsNone(state.add("not-runtime", row))
                self.assertEqual(state.jobs(), [])

    def test_dry_run_makes_zero_subprocess_calls(self):
        with TemporaryDirectory() as tmp:
            with repair.State(Path(tmp)) as state:
                key = state.add("first", incident())
                with mock.patch.object(repair, "shell", side_effect=AssertionError("Paid tool called")):
                    report = repair.run_one(state, key, None, False, False, 1)
                self.assertIn("DRY_RUN", report["next"])
                self.assertEqual(state.job(key)["calls"], 0)

    def test_explicit_cost_ack_required(self):
        with TemporaryDirectory() as tmp:
            with repair.State(Path(tmp)) as state:
                key = state.add("first", incident())
                with self.assertRaisesRegex(ValueError, "acknowledge-cost"):
                    repair.run_one(state, key, Path(tmp), True, False, 1)
                self.assertEqual(state.job(key)["calls"], 0)

    def test_quota_and_one_shot_veto(self):
        with TemporaryDirectory() as tmp:
            with repair.State(Path(tmp)) as state:
                a = state.add("one", incident())
                b = state.add("two", incident("전혀 다른 오류"))
                state.reserve(a, 1)
                with self.assertRaisesRegex(ValueError, "Daily"):
                    state.reserve(b, 1)
                with self.assertRaisesRegex(ValueError, "repeated"):
                    state.reserve(a, 3)
                self.assertEqual(state.job(b)["calls"], 0)

    def test_existing_registered_modes(self):
        self.assertEqual(repair.suite_for("다중가공"), "tests/altering/Regression.csproj")
        self.assertEqual(repair.suite_for("자동 채집"), "tests/gathering/Regression.csproj")
        self.assertEqual(repair.suite_for("어비스"), "tests/loot-retry/Regression.csproj")
        self.assertIsNone(repair.suite_for("알 수 없음"))

    def test_local_actual_github_layout(self):
        with TemporaryDirectory() as tmp:
            root = Path(tmp)
            file = root / "runtime-errors/2026-10-24/20261024_153253_640_다중가공_오류_3f0cd6/summary.json"
            file.parent.mkdir(parents=True)
            file.write_text(json.dumps(incident(), ensure_ascii=False), encoding="utf-8")
            self.assertEqual(len(repair.fetch_local(root, 10)), 1)

    def test_github_reads_only_summaries_not_images_or_logs(self):
        tree = {"truncated": False, "tree": [
            {"type": "blob", "path": "runtime-errors/2026-10-24/x/summary.json"},
            {"type": "blob", "path": "runtime-errors/2026-10-24/x/screen.png"},
            {"type": "blob", "path": "runtime-errors/2026-10-24/x/recent.log"},
            {"type": "blob", "path": "test-runs/2026-10-24/x/summary.json"}]}
        def fake(url, size):
            return tree if url == repair.TREE else incident()
        with mock.patch.object(repair, "get_json", side_effect=fake) as get:
            result = repair.fetch_github(5)
        self.assertEqual(len(result), 1)
        self.assertEqual(get.call_count, 2)
        self.assertTrue(all("screen.png" not in call.args[0] and "recent.log" not in call.args[0]
                            for call in get.call_args_list))

    def test_worktree_scope_blocks_updater_changes(self):
        with TemporaryDirectory() as tmp:
            value = subprocess.CompletedProcess([], 0, " M source/tools/ApplyUpdate.ps1\n", "")
            with mock.patch.object(repair, "shell", return_value=value):
                with self.assertRaisesRegex(ValueError, "Forbidden"):
                    repair.candidate_files(Path(tmp))

    def test_one_shot_synthetic_code_test_candidate_flow(self):
        with TemporaryDirectory() as tmp:
            root = Path(tmp)
            src = root / "src"
            csproj = src / "source/FishingAutomation/FishingAutomation.csproj"
            csproj.parent.mkdir(parents=True)
            csproj.write_text("<Project/>", encoding="utf-8")
            with repair.State(root / "state") as state:
                key = state.add("fixture", incident())
                calls = []
                sha = "a" * 40
                def fake(command, cwd, timeout=30, prompt=None):
                    calls.append(command[0:2])
                    if command[:2] == ["git", "rev-parse"]:
                        return subprocess.CompletedProcess(command, 0, sha + "\n", "")
                    if command[:2] == ["git", "status"]:
                        changes = "" if "workspaces" not in str(cwd) else (
                            " M source/FishingAutomation/AlteringPlan.cs\n"
                            " M tests/altering/Program.cs\n")
                        return subprocess.CompletedProcess(command, 0, changes, "")
                    if command[:3] == ["git", "worktree", "add"]:
                        Path(command[4]).mkdir(parents=True)
                    return subprocess.CompletedProcess(command, 0, "", "")
                with mock.patch.object(repair.shutil, "which", return_value="/mock/tool"), \
                     mock.patch.object(repair, "shell", side_effect=fake):
                    result = repair.run_one(state, key, src, True, True, 1)
                self.assertEqual(result["status"], "candidate_passed")
                self.assertEqual(state.job(key)["calls"], 1)
                self.assertEqual(state.job(key)["status"], "candidate_passed")
                self.assertEqual(len([c for c in calls if c[0] == "codex"]), 1)
                self.assertEqual(len([c for c in calls if c[0] == "dotnet"]), 1)
                with self.assertRaisesRegex(ValueError, "repeated"):
                    state.reserve(key, 3)
                self.assertTrue((root / "state" / "workspaces" / key / "result.json").exists())


    if __name__ == "__main__":
        main()
