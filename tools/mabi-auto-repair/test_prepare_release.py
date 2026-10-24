"""Offline acceptance of next-version preparation; no GitHub/game/release calls."""
from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

import prepare_release as stage


HEAD = "a" * 40
MERGE = "b" * 40


class ReleasePrepTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        def put(path, value):
            target = self.root / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(value, encoding="utf-8")
        put("source/FishingAutomation/UpdateManager.cs",
            'public const string CurrentVersion = "V3.2.62";')
        put("source/FishingAutomation/FishingAutomation.csproj",
            "<Version>3.2.62</Version>\n"
            "<AssemblyVersion>3.2.62.0</AssemblyVersion>\n"
            "<FileVersion>3.2.62.0</FileVersion>\n")
        put(".github/workflows/ci.yml",
            'on:\n  push:\n    branches: [main]\n'
            '''if ($u -notmatch 'CurrentVersion = "V3\\.2\\.62"') { throw 'bad' }\n'''
            '''if ($p -notmatch '<Version>3\\.2\\.62</Version>') { throw 'bad' }\n'''
            "$release = Get-Content .github/workflows/release-v3262-auto-update.yml -Raw\n")
        put(".github/workflows/release-v3262-auto-update.yml",
            "name: Publish V3.2.62 Legacy AutoFix Retirement Auto Update After Main CI\n"
            "on:\n  workflow_run:\n"
            "    github.event.workflow_run.event == 'push'\n"
            "concurrency:\n  group: v3262-auto-update-release\n"
            "      - name: Require game signoff OR direct-user-requested unverified rollout\n"
            "        shell: pwsh\n        run: |\n          Write-Host 'Game requirement'\n"
            "      - name: Require CI-approved current main and exact V3.2.62 version\n"
            '''          if ($u -notmatch 'CurrentVersion = "V3\\.2\\.62"') { throw 'bad' }\n'''
            "          Write-Host 'MabiAuto_V3.2.62_Windows_Lite.zip'\n"
            "          Write-Host '<Version>3.2.62</Version>'\n")

    def test_ci_only_version_stage_uses_next_patch_and_does_not_claim_game_verified(self):
        staged = stage.prepare(self.root, "V3.2.62", 4321, HEAD, MERGE)
        self.assertEqual(staged["new_version"], "V3.2.63")
        self.assertFalse(staged["real_game_verified"])
        self.assertEqual(len(staged["files"]), 6)
        updater = (self.root / "source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8")
        self.assertIn('CurrentVersion = "V3.2.63"', updater)
        ci = (self.root / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        self.assertIn("  workflow_dispatch:", ci)
        self.assertIn("release-v3263-auto-update.yml", ci)
        self.assertIn("V3\\.2\\.63", ci)
        workflow = (self.root / ".github/workflows/release-v3263-auto-update.yml").read_text(encoding="utf-8")
        self.assertIn("V3.2.63", workflow)
        self.assertIn("MabiAuto_V3.2.63_Windows_Lite.zip", workflow)
        self.assertIn("ci_only_unverified", workflow)
        self.assertNotIn("Require game signoff OR", workflow)
        self.assertIn("workflow_dispatch", workflow)
        approval = json.loads((self.root / "source/V3.2.63_CI_ONLY_REPAIR_APPROVAL.json").read_text(encoding="utf-8"))
        self.assertEqual(approval["approved_head_sha"], HEAD)
        self.assertEqual(approval["reviewed_merge_sha"], MERGE)
        self.assertFalse(approval["real_game_verified"])
        self.assertTrue(approval["operator_reviewed"])
        self.assertIn("not tested", (self.root / "source/RELEASE_NOTES_V3.2.63.md").read_text(encoding="utf-8"))

    def test_refuse_source_public_version_mismatch_without_changes(self):
        with self.assertRaisesRegex(stage.ReleasePrepError, "must equal"):
            stage.prepare(self.root, "V3.2.61", 4321, HEAD, MERGE)
        self.assertFalse((self.root / "source/V3.2.63_CI_ONLY_REPAIR_APPROVAL.json").exists())

    def test_refuse_duplicate_release_assets_without_overwriting(self):
        p = self.root / "source/RELEASE_NOTES_V3.2.63.md"
        p.write_text("keep-original")
        with self.assertRaisesRegex(stage.ReleasePrepError, "already exist"):
            stage.prepare(self.root, "V3.2.62", 4321, HEAD, MERGE)
        self.assertEqual(p.read_text(), "keep-original")

    def test_refuse_bad_approved_sha(self):
        with self.assertRaisesRegex(stage.ReleasePrepError, "40-character"):
            stage.prepare(self.root, "V3.2.62", 4321, "unsafe", MERGE)
        self.assertFalse((self.root / "source/V3.2.63_CI_ONLY_REPAIR_APPROVAL.json").exists())

    def test_real_repository_release_template_stages_next_version_offline(self):
        # Exercise the actual checked-in ~280-line packaging workflow and
        # >1000-line production CI version contract, never an invented sample.
        source = Path(__file__).resolve().parents[2]
        needed = (
            "source/FishingAutomation/UpdateManager.cs",
            "source/FishingAutomation/FishingAutomation.csproj",
            ".github/workflows/ci.yml",
            ".github/workflows/release-v3262-auto-update.yml",
        )
        if not all((source / n).is_file() for n in needed):
            self.skipTest("Actual source tree unavailable")
        for relative in needed:
            target = self.root / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes((source / relative).read_bytes())
        outcome = stage.prepare(self.root, "V3.2.62", 4321, HEAD, MERGE)
        release = (self.root / ".github/workflows/release-v3263-auto-update.yml").read_text(encoding="utf-8")
        self.assertEqual(outcome["new_version"], "V3.2.63")
        self.assertIn("MabiAuto_V3.2.63_Windows_Lite.zip.sha256", release)
        self.assertIn("RollbackPrevious.ps1", release)
        self.assertIn("reviewed repair and explicit CI-only", release)
        self.assertNotIn("V3.2.62_Windows_Lite.zip", release)
        self.assertIn("workflow_dispatch", release)
        self.assertIn("release-v3263-auto-update.yml",
                      (self.root / ".github/workflows/ci.yml").read_text(encoding="utf-8"))

    def test_next_patch_sequence_is_monotonic_and_requires_published_release(self):
        self.assertEqual(stage.next_tag("V3.2.63", "V3.2.63"), "V3.2.64")
        with self.assertRaises(stage.ReleasePrepError):
            stage.next_tag("V3.2.63", "V3.2.62")
        with self.assertRaises(stage.ReleasePrepError):
            stage.next_tag("not-a-version", "not-a-version")


if __name__ == "__main__":
    unittest.main()
