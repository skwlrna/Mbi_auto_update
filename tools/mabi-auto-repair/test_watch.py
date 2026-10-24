"""Offline acceptance for the scheduled repair watcher.

Uses real temp incident folders and SQLite, but never Codex or the live game.
"""
from __future__ import annotations

import io
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import repair
import watch


def incident(version="V3.2.62", detail="HUD screen recognition failed"):
    return {"schema": 1, "source": "runtime", "version": version,
            "title": "test altering", "detail": detail, "mode": "가공"}


def write_incident(log_root: Path, day: str, unique: str, **kwargs):
    path = (log_root / "runtime-errors" / day / unique / "summary.json")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(incident(**kwargs)), encoding="utf-8")
    return "local:" + path.relative_to(log_root).as_posix()


def version_checkout(root: Path, version="V3.2.62"):
    source = root / "source/FishingAutomation"
    source.mkdir(parents=True, exist_ok=True)
    (source / "UpdateManager.cs").write_text(
        f'public const string CurrentVersion = "{version}";', encoding="utf-8")
    return root


class WatchTests(unittest.TestCase):
    def test_first_init_baselines_all_existing_and_is_repeat_safe(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            logs = root / "log"
            for i in range(30):
                write_incident(logs, "2026-10-23", f"old-{i:03d}")
            checkout = version_checkout(root / "app")
            with repair.State(root / "state") as state:
                self.assertEqual(watch.initialize(state, logs)["baseline_count"], 30)
                newest = write_incident(logs, "2026-10-24", "new-001")
                self.assertEqual(watch.initialize(state, logs)["status"], "already_initialized")
                with mock.patch.object(watch.pipeline, "investigate",
                                       side_effect=AssertionError("Dry run must not call AI")):
                    result = watch.poll(state, logs, checkout, False, False)
                self.assertEqual(result["new_count"], 1)
                self.assertEqual(result["outcomes"], {"observed_unpaid": 1})
                self.assertEqual(result["new_events"][0]["source"], newest)
                self.assertEqual(watch.poll(state, logs, checkout, False, False)["new_count"], 0)
                self.assertEqual(watch.report_status(state)["counts"]["baseline"], 30)

    def test_same_version_new_error_autodiagnosed_only_once(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            logs = root / "log"
            write_incident(logs, "2026-10-22", "old", version="V3.2.61")
            checkout = version_checkout(root / "app")
            with repair.State(root / "state") as state:
                watch.initialize(state, logs)
                path = write_incident(logs, "2026-10-24", "fresh")
                outcomes = []
                def fake_investigate(*args, **kwargs):
                    outcomes.append(args[1])
                    return {"status": "candidate_passed",
                            "image_calls": 1, "patch_calls_reserved": 1}
                with mock.patch.object(watch.pipeline, "investigate",
                                       side_effect=fake_investigate), \
                     mock.patch.object(watch.repair, "clean_git_repo", return_value="a" * 40), \
                     mock.patch.object(watch.shutil, "which", return_value="installed"):
                    result = watch.poll(state, logs, checkout, True, True)
                    self.assertEqual(result["outcomes"], {"candidate_passed": 1})
                    self.assertEqual(len(outcomes), 1)
                    self.assertEqual(result["new_events"][0]["source"], path)
                    self.assertEqual(watch.poll(state, logs, checkout, True, True)["new_count"], 0)
                    self.assertEqual(len(outcomes), 1)
                    duplicate = write_incident(logs, "2026-10-24", "fresh-duplicate")
                    second = watch.poll(state, logs, checkout, True, True)
                self.assertEqual(second["outcomes"], {"duplicate_fingerprint": 1})
                self.assertEqual(second["new_events"][0]["source"], duplicate)
                self.assertEqual(len(outcomes), 1)
                self.assertEqual(state.job(outcomes[0])["calls"], 0,
                                 "Fake pipeline never booked a paid call")

    def test_old_version_does_not_use_codex_and_new_matching_version_can_run(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            logs = root / "log"
            logs.mkdir()
            checkout = version_checkout(root / "app", "V3.2.62")
            with repair.State(root / "state") as state:
                watch.initialize(state, logs)
                write_incident(logs, "2026-10-24", "old-event", version="V3.2.61")
                with mock.patch.object(watch.pipeline, "investigate",
                                       side_effect=AssertionError("old errors must not spend")):
                    result = watch.poll(state, logs, checkout, True, True)
                self.assertEqual(result["outcomes"], {"skipped_version": 1})
                write_incident(logs, "2026-10-24", "current-event", version="V3.2.62")
                seen = []
                with mock.patch.object(watch.pipeline, "investigate",
                                       side_effect=lambda *a, **k: (seen.append(a[1]) or {"status": "diagnosed_no_patch"})), \
                     mock.patch.object(watch.repair, "clean_git_repo", return_value="a" * 40), \
                     mock.patch.object(watch.shutil, "which", return_value="installed"):
                    current = watch.poll(state, logs, checkout, True, True)
                self.assertEqual(current["outcomes"], {"diagnosed_no_patch": 1})
                self.assertEqual(len(seen), 1)

    def test_failure_claimed_persists_no_silent_retry(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            logs = root / "log"
            logs.mkdir()
            checkout = version_checkout(root / "app")
            with repair.State(root / "state") as state:
                watch.initialize(state, logs)
                write_incident(logs, "2026-10-24", "fail-first")
                with mock.patch.object(watch.pipeline, "investigate",
                                       side_effect=OSError("Codex disconnected")), \
                     mock.patch.object(watch.repair, "clean_git_repo", return_value="a" * 40), \
                     mock.patch.object(watch.shutil, "which", return_value="installed"):
                    result = watch.poll(state, logs, checkout, True, True)
                self.assertEqual(result["outcomes"], {"needs_review": 1})
                self.assertEqual(watch.poll(state, logs, checkout, False, False)["new_count"], 0)

    def test_no_paid_task_without_explicit_ack(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            log = root / "log"
            log.mkdir()
            checkout = version_checkout(root / "app")
            with repair.State(root / "state") as state:
                watch.initialize(state, log)
                with self.assertRaisesRegex(ValueError, "acknowledge-cost"):
                    watch.poll(state, log, checkout, True, False)

    def test_claim_transaction_blocks_second_process(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            with repair.State(root / "state") as state:
                watch.prepare(state)
                s = "local:runtime-errors/2026-10-24/fresh/summary.json"
                self.assertEqual(watch.claim_event(state, s, "fingerprint", True), "claimed")
                self.assertEqual(watch.claim_event(state, s, "fingerprint", True), "duplicate_source")
                s2 = s.replace("/fresh/", "/another/")
                self.assertEqual(watch.claim_event(state, s2, "fingerprint", True),
                                 "duplicate_fingerprint")
                self.assertEqual(watch.report_status(state)["counts"]["claimed"], 1)

    def test_cli_init_poll_status_without_git_pulls_and_costs(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            logs = root / "logs"
            logs.mkdir()
            checkout = version_checkout(root / "app")
            original = ["--state", str(root / "state")]
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                self.assertEqual(watch.main(original + ["init", "--log-root", str(logs),
                                                         "--no-pull"]), 0)
            self.assertEqual(json.loads(stdout.getvalue())["status"], "initialized")
            write_incident(logs, "2026-10-24", "first-new")
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                self.assertEqual(watch.main(original + ["poll", "--log-root", str(logs),
                                                         "--repo", str(checkout), "--no-pull"]), 0)
            self.assertEqual(json.loads(stdout.getvalue())["outcomes"], {"observed_unpaid": 1})
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                self.assertEqual(watch.main(original + ["status"]), 0)
            self.assertEqual(json.loads(stdout.getvalue())["counts"]["observed_unpaid"], 1)

    def test_git_pull_does_not_write_to_log_repository_other_than_fast_forward(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp)
            (path / ".git").mkdir()
            with mock.patch.object(watch.subprocess, "run",
                    return_value=subprocess.CompletedProcess([], 0, "", "")) as p:
                watch.pull_logs(path)
            self.assertEqual(p.call_args.args[0][0:2], ["git", "-C"])
            self.assertEqual(p.call_args.args[0][-2:], ["pull", "--ff-only"])
            with mock.patch.object(watch.subprocess, "run",
                    return_value=subprocess.CompletedProcess([], 1, "", "credential denied")):
                with self.assertRaisesRegex(ValueError, "Git log pull failed"):
                    watch.pull_logs(path)


    def test_paid_new_error_creates_at_most_one_draft_pr(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            logs = root / "log"
            logs.mkdir()
            checkout = version_checkout(root / "app")
            with repair.State(root / "state") as state:
                watch.initialize(state, logs)
                source = write_incident(logs, "2026-10-24", "fresh-for-pr")
                calls = []
                def mocked_pipeline(*args, **kwargs):
                    calls.append(args[1])
                    return {"status": "candidate_passed",
                            "candidate_verified": True}
                link = "https://github.com/skwlrna/Mbi_auto_update/pull/54321"
                with mock.patch.object(watch.pipeline, "investigate",
                                       side_effect=mocked_pipeline), \
                     mock.patch.object(watch.repair, "clean_git_repo", return_value="a" * 40), \
                     mock.patch.object(watch.shutil, "which", return_value="installed"), \
                     mock.patch.object(watch.publish_candidate, "command", return_value=""), \
                     mock.patch.object(watch.publish_candidate, "publish", return_value=link) as pub:
                    first = watch.poll(state, logs, checkout, True, True, auto_pr=True)
                    self.assertEqual(first["outcomes"], {"pr_opened": 1})
                    self.assertEqual(first["new_events"][0]["pr_url"], link)
                    self.assertEqual(first["new_events"][0]["source"], source)
                    pub.assert_called_once()
                    second = watch.poll(state, logs, checkout, True, True, auto_pr=True)
                    self.assertEqual(second["new_count"], 0)
                    self.assertEqual(len(calls), 1)
                    self.assertEqual(pub.call_count, 1)
                record = watch.report_status(state)
                self.assertEqual(record["counts"]["pr_opened"], 1)
                self.assertEqual(record["recent_events"][0]["pr_url"], link)

    def test_failed_publish_requires_review_and_never_retries(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            logs = root / "logs"
            logs.mkdir()
            checkout = version_checkout(root / "app")
            with repair.State(root / "state") as state:
                watch.initialize(state, logs)
                write_incident(logs, "2026-10-24", "new-source")
                with mock.patch.object(watch.pipeline, "investigate",
                                       return_value={"status": "candidate_passed"}), \
                     mock.patch.object(watch.repair, "clean_git_repo",
                                       return_value="b" * 40), \
                     mock.patch.object(watch.shutil, "which", return_value="installed"), \
                     mock.patch.object(watch.publish_candidate, "command", return_value=""), \
                     mock.patch.object(watch.publish_candidate, "publish",
                                       side_effect=watch.publish_candidate.PublishBlocked(
                                           "Not authorized")) as pub:
                    result = watch.poll(state, logs, checkout, True, True, auto_pr=True)
                    self.assertEqual(result["outcomes"], {"pr_needs_review": 1})
                    self.assertEqual(result["new_events"][0]["reason"], "Not authorized")
                    again = watch.poll(state, logs, checkout, True, True, auto_pr=True)
                    self.assertEqual(again["new_count"], 0)
                    pub.assert_called_once()
                self.assertEqual(watch.report_status(state)["counts"]["pr_needs_review"], 1)

    def test_auto_pr_must_be_explicit_and_gh_available_before_ai(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            logs = root / "log"
            logs.mkdir()
            checkout = version_checkout(root / "app")
            with repair.State(root / "state") as state:
                watch.initialize(state, logs)
                write_incident(logs, "2026-10-24", "new")
                with self.assertRaisesRegex(ValueError, "requires --execute"):
                    watch.poll(state, logs, checkout, False, False, auto_pr=True)
                with mock.patch.object(watch.shutil, "which", return_value=None):
                    with self.assertRaisesRegex(ValueError, "GitHub CLI gh required"):
                        watch.poll(state, logs, checkout, True, True, auto_pr=True)
                with mock.patch.object(watch.pipeline, "investigate",
                                       side_effect=AssertionError("Never invoke model")), \
                     mock.patch.object(watch.shutil, "which", return_value="installed"), \
                     mock.patch.object(watch.publish_candidate, "command",
                                       side_effect=watch.publish_candidate.PublishBlocked(
                                           "not logged in")):
                    with self.assertRaises(watch.publish_candidate.PublishBlocked):
                        watch.poll(state, logs, checkout, True, True, auto_pr=True)
                self.assertEqual(watch.report_status(state)["counts"], {})


if __name__ == "__main__":
    unittest.main()
