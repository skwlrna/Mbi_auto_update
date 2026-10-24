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
            version_file = src / "source/FishingAutomation/UpdateManager.cs"
            version_file.write_text('public const string CurrentVersion = "V3.2.60";',
                                    encoding="utf-8")
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


    def test_latest_github_incident_replay_blocks_paid_codex(self):
        # Real Mbi_auto_log runtime summary, with private absolute paths removed.
        fixtures = Path(__file__).resolve().parent / "fixtures"
        records = repair.fetch_local(fixtures, 10)
        self.assertEqual(len(records), 1)
        source, incident = records[0]
        self.assertEqual(incident["title"], "다중가공 오류")
        self.assertEqual(incident["version"], "V3.2.60")
        with TemporaryDirectory() as tmp:
            root = Path(tmp)
            code = root / "app"
            version = code / "source/FishingAutomation/UpdateManager.cs"
            version.parent.mkdir(parents=True)
            version.write_text('public const string CurrentVersion = "V3.2.62";', encoding="utf-8")
            with repair.State(root / "db") as state:
                key = state.add(source, incident)
                with mock.patch.object(repair, "shell",
                                       side_effect=AssertionError("No subprocess expected")):
                    preview = repair.run_one(state, key, code, False, False, 1)
                    with self.assertRaisesRegex(ValueError, "Preflight blocked before AI"):
                        repair.run_one(state, key, code, True, True, 1)
                self.assertEqual(preview["source_version"], "V3.2.62")
                self.assertEqual(preview["error_version"], "V3.2.60")
                self.assertEqual(set(preview["preflight_reasons"]),
                                 {"source_version_not_reproduced", "visual_evidence_missing"})
                self.assertEqual(state.job(key)["calls"], 0)

    def test_visual_log_without_photo_stays_blocked_even_on_same_version(self):
        with TemporaryDirectory() as tmp:
            root = Path(tmp)
            code = root / "app"
            version = code / "source/FishingAutomation/UpdateManager.cs"
            version.parent.mkdir(parents=True)
            version.write_text('public const string CurrentVersion = "V3.2.60";', encoding="utf-8")
            with repair.State(root / "db") as state:
                key = state.add("image-gap", incident("실제 시설창의 독립 UI 구조 확인 불가"))
                output = repair.run_one(state, key, code, False, False, 1)
                self.assertEqual(output["preflight_reasons"], ["visual_evidence_missing"])
                self.assertEqual(output["codex_calls"], 0)
                with self.assertRaisesRegex(ValueError, "visual_evidence_missing"):
                    repair.run_one(state, key, code, True, True, 1)
                self.assertEqual(state.job(key)["calls"], 0)

    def test_two_recent_real_github_errors_import_and_preflight_without_codex(self):
        # Anonymized from Mbi_auto_log 2026-10-24 18:13 / 18:35 KST.
        from pathlib import Path
        from tempfile import TemporaryDirectory
        import json
        import subprocess
        import sys
        fixtures = Path(__file__).resolve().parent / "fixtures-recent"
        loaded = repair.fetch_local(fixtures, 10)
        self.assertEqual(len(loaded), 2)
        by_version = {origin: data for origin, data in loaded}
        self.assertTrue(all(item["version"] == "V3.2.61" for item in by_version.values()))
        self.assertTrue(all(item["mode"] == "가공" for item in by_version.values()))
        with TemporaryDirectory() as tmp:
            root = Path(tmp)
            code = root / "code"
            version = code / "source/FishingAutomation/UpdateManager.cs"
            version.parent.mkdir(parents=True)
            version.write_text(
                'public const string CurrentVersion = "V3.2.62";',
                encoding="utf-8")
            with repair.State(root / "db") as state:
                keys = [state.add(source, record) for source, record in loaded]
                self.assertEqual(len(set(keys)), 2, "Two different failure stages")
                # Importing the same actual source paths again must not increase hits.
                for source, record in loaded:
                    state.add(source, record)
                self.assertEqual(len(state.jobs()), 2)
                self.assertTrue(all(j["hits"] == 1 for j in state.jobs()))
                with mock.patch.object(repair, "shell",
                                       side_effect=AssertionError("Unexpected paid execution")):
                    for key in keys:
                        preview = repair.run_one(state, key, code, False, False, 1)
                        self.assertFalse(preview["ready_for_ai"])
                        self.assertEqual(preview["source_version"], "V3.2.62")
                        self.assertEqual(preview["error_version"], "V3.2.61")
                        self.assertEqual(
                            set(preview["preflight_reasons"]),
                            {"source_version_not_reproduced", "visual_evidence_missing"})
                        with self.assertRaisesRegex(ValueError, "Preflight blocked before AI"):
                            repair.run_one(state, key, code, True, True, 1)
                self.assertEqual(sum(j["calls"] for j in state.jobs()), 0)
                self.assertTrue(all(j["status"] == "new" for j in state.jobs()))
            # Simulate a matching source version: lack of image access STILL blocks.
            version.write_text(
                'public const string CurrentVersion = "V3.2.61";',
                encoding="utf-8")
            with repair.State(root / "db") as state:
                with mock.patch.object(repair, "shell",
                                       side_effect=AssertionError("No Codex allowed")):
                    for key in keys:
                        preview = repair.run_one(state, key, code, False, False, 1)
                        self.assertEqual(
                            preview["preflight_reasons"], ["visual_evidence_missing"])
                        with self.assertRaisesRegex(ValueError, "visual_evidence_missing"):
                            repair.run_one(state, key, code, True, True, 1)
                self.assertEqual(sum(j["calls"] for j in state.jobs()), 0)

    def test_latest_real_github_log_cli_sync_status_and_dry_run(self):
        import subprocess
        import sys
        with TemporaryDirectory() as tmp:
            root = Path(tmp)
            script = Path(repair.__file__).resolve()
            fixtures = script.parent / "fixtures-recent"
            state_dir = root / "state"
            command = [sys.executable, str(script), "--state", str(state_dir)]
            sync = subprocess.run(command + ["sync", "--dir", str(fixtures), "--limit", "10"],
                                  text=True, capture_output=True, timeout=30)
            self.assertEqual(sync.returncode, 0, sync.stderr)
            payload = repair.json.loads(sync.stdout)
            self.assertEqual(payload["read"], 2)
            self.assertEqual(payload["accepted"], 2)
            self.assertEqual(payload["unique"], 2)
            self.assertEqual(payload["codex_calls"], 0)
            again = subprocess.run(command + ["sync", "--dir", str(fixtures), "--limit", "10"],
                                   text=True, capture_output=True, timeout=30)
            self.assertEqual(again.returncode, 0)
            status = subprocess.run(command + ["status"], text=True,
                                    capture_output=True, timeout=30)
            self.assertEqual(status.returncode, 0, status.stderr)
            jobs = repair.json.loads(status.stdout)["jobs"]
            self.assertEqual(len(jobs), 2)
            self.assertTrue(all(j["hits"] == 1 and j["calls"] == 0 for j in jobs))
            for job in jobs:
                preview = subprocess.run(command + ["run", job["id"]], text=True,
                                         capture_output=True, timeout=30)
                self.assertEqual(preview.returncode, 0, preview.stderr)
                output = repair.json.loads(preview.stdout)
                self.assertFalse(output["ready_for_ai"])
                self.assertEqual(output["codex_calls"], 0)
                self.assertIn("DRY_RUN", output["next"])

    def test_same_source_reimport_never_misattributes_new_payload(self):
        with TemporaryDirectory() as tmp:
            with repair.State(Path(tmp)) as state:
                original = state.add("one-source", incident("First cause"))
                reimport = state.add("one-source", incident("Different later payload"))
                self.assertEqual(original, reimport)
                self.assertEqual(len(state.jobs()), 1)
                self.assertEqual(state.job(original)["hits"], 1)


if __name__ == "__main__":
    main()
