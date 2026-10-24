"""Read-only Windows operator readiness and CI-only review messaging contracts."""
import unittest
from pathlib import Path

import watch

PATH = Path(__file__).resolve().parent / "doctor.ps1"


class DoctorContracts(unittest.TestCase):
    def test_doctor_is_explicitly_read_only(self):
        source = PATH.read_text(encoding="utf-8-sig")
        for forbidden in (
            "Register-ScheduledTask", "Unregister-ScheduledTask",
            "Remove-Item ", "Set-ScheduledTask", "Start-ScheduledTask",
            "git pull", "git push", "codex exec",
            "gh pr merge", "gh release create", "Start-Process ",
        ):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, source)
        for phrase in (
            "Get-ScheduledTask", "Get-ScheduledTaskInfo",
            "Get-Command", "git -C $repo status --porcelain",
            "gh auth status", "watch.py", "--auto-pr",
            "--acknowledge-cost", "ConvertTo-Json",
            "live_game_or_macro_update_tested = $false",
        ):
            with self.subTest(phrase=phrase):
                self.assertIn(phrase, source)

    def test_ci_review_is_not_blocked_on_game_test(self):
        self.assertIn("promote-ci-only", watch.NEXT_ACTION["ci_passed_waiting_game"])
        self.assertIn("no game test required", watch.NEXT_ACTION["ci_passed_waiting_game"])
        self.assertIn("Windows CI", watch.NEXT_ACTION["pr_opened"])
        self.assertNotIn("verify actual game", watch.NEXT_ACTION["candidate_passed"])


if __name__ == "__main__":
    unittest.main()
