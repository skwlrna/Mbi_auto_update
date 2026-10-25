"""Offline acceptance: version-only release changes never touch workflow YAML."""
from __future__ import annotations

import json
import re
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

        def put(name, data):
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(data, encoding="utf-8")

        put("source/FishingAutomation/UpdateManager.cs",
            'public const string CurrentVersion = "V3.2.62";')
        put("source/FishingAutomation/FishingAutomation.csproj",
            "<Version>3.2.62</Version>\n"
            "<AssemblyVersion>3.2.62.0</AssemblyVersion>\n"
            "<FileVersion>3.2.62.0</FileVersion>\n")
        put(".github/workflows/publish-reviewed-repair-auto-update.yml",
            "name: Static reviewed repair release\n")
        put(".github/workflows/ci.yml", "name: Static CI workflow\n")

    def test_first_stage_modifies_only_four_version_metadata_files(self):
        result = stage.prepare(self.root, "V3.2.62", 4321, HEAD, MERGE)
        self.assertEqual(result["new_version"], "V3.2.63")
        self.assertEqual(len(result["files"]), 4)
        self.assertFalse(result["real_game_verified"])
        self.assertTrue(all(not name.startswith(".github") for name in result["files"]))
        self.assertEqual(result["release_workflow"],
                         "publish-reviewed-repair-auto-update.yml")
        updated = (self.root / "source/FishingAutomation/UpdateManager.cs").read_text(
            encoding="utf-8")
        self.assertIn('CurrentVersion = "V3.2.63"', updated)
        project = (self.root / "source/FishingAutomation/FishingAutomation.csproj").read_text(
            encoding="utf-8")
        self.assertIn("<Version>3.2.63</Version>", project)
        self.assertIn("<FileVersion>3.2.63.0</FileVersion>", project)
        self.assertEqual((self.root / ".github/workflows/ci.yml").read_text(encoding="utf-8"),
                         "name: Static CI workflow\n")
        self.assertEqual((self.root /
            ".github/workflows/publish-reviewed-repair-auto-update.yml").read_text(
                encoding="utf-8"), "name: Static reviewed repair release\n")
        approval = json.loads((self.root /
            "source/V3.2.63_CI_ONLY_REPAIR_APPROVAL.json").read_text(encoding="utf-8"))
        self.assertEqual(approval["approved_head_sha"], HEAD)
        self.assertEqual(approval["reviewed_merge_sha"], MERGE)
        self.assertFalse(approval["real_game_verified"])
        self.assertTrue(approval["operator_reviewed"])
        self.assertTrue(approval["rollback_required"])
        notes = (self.root / "source/RELEASE_NOTES_V3.2.63.md").read_text(encoding="utf-8")
        self.assertIn("not tested", notes)

    def test_second_release_works_without_workflow_regeneration(self):
        stage.prepare(self.root, "V3.2.62", 4321, HEAD, MERGE)
        result = stage.prepare(self.root, "V3.2.63", 4322, "c" * 40, "d" * 40)
        self.assertEqual(result["new_version"], "V3.2.64")
        self.assertEqual(len(result["files"]), 4)
        self.assertIn("<Version>3.2.64</Version>",
            (self.root / "source/FishingAutomation/FishingAutomation.csproj").read_text(
                encoding="utf-8"))
        self.assertEqual((self.root / ".github/workflows/ci.yml").read_text(encoding="utf-8"),
                         "name: Static CI workflow\n")

    def test_version_mismatch_causes_no_write(self):
        with self.assertRaisesRegex(stage.ReleasePrepError, "must equal"):
            stage.prepare(self.root, "V3.2.61", 4321, HEAD, MERGE)
        self.assertFalse((self.root / "source/V3.2.63_CI_ONLY_REPAIR_APPROVAL.json").exists())

    def test_duplicate_assets_cannot_be_overwritten(self):
        file = self.root / "source/RELEASE_NOTES_V3.2.63.md"
        file.write_text("original", encoding="utf-8")
        with self.assertRaisesRegex(stage.ReleasePrepError, "already exist"):
            stage.prepare(self.root, "V3.2.62", 4321, HEAD, MERGE)
        self.assertEqual(file.read_text(encoding="utf-8"), "original")

    def test_invalid_head_sha_fails_closed(self):
        with self.assertRaisesRegex(stage.ReleasePrepError, "40-character"):
            stage.prepare(self.root, "V3.2.62", 4321, "oops", MERGE)

    def test_current_repository_contract_works_without_mutating_ci(self):
        # Run the real production source through the version preparer, but
        # never push, publish, dispatch, or access the game.
        source = Path(__file__).resolve().parents[2]
        required = (
            "source/FishingAutomation/UpdateManager.cs",
            "source/FishingAutomation/FishingAutomation.csproj",
            ".github/workflows/ci.yml",
            ".github/workflows/publish-reviewed-repair-auto-update.yml",
        )
        if not all((source / file).is_file() for file in required):
            self.skipTest("Not running inside complete repository")
        for file in required:
            target = self.root / file
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes((source / file).read_bytes())
        ci_before = (self.root / ".github/workflows/ci.yml").read_bytes()
        publisher_before = (self.root /
            ".github/workflows/publish-reviewed-repair-auto-update.yml").read_bytes()
        # Read the ACTUAL checkout version instead of assuming a stale
        # public V3.2.62. This contract must keep passing after every
        # successful automatically published patch release.
        update_source = (self.root / "source/FishingAutomation/UpdateManager.cs").read_text(
            encoding="utf-8-sig")
        match = re.search(r'CurrentVersion = "(V\d+\.\d+\.\d+)"', update_source)
        self.assertIsNotNone(match)
        published = match.group(1)
        result = stage.prepare(self.root, published, 4321, HEAD, MERGE)
        self.assertEqual(result["new_version"], stage.next_tag(published, published))
        self.assertEqual(ci_before, (self.root / ".github/workflows/ci.yml").read_bytes())
        self.assertEqual(publisher_before, (self.root /
            ".github/workflows/publish-reviewed-repair-auto-update.yml").read_bytes())
        published = publisher_before.decode("utf-8-sig")
        self.assertIn("RollbackPrevious.ps1", published)
        self.assertIn("Windows_Lite.zip.sha256", published)
        self.assertIn("reviewed=true", published)
        self.assertIn("workflow_run", published)

    def test_next_patch_version_is_monotonic(self):
        self.assertEqual(stage.next_tag("V3.2.63", "V3.2.63"), "V3.2.64")
        with self.assertRaises(stage.ReleasePrepError):
            stage.next_tag("V3.2.63", "V3.2.62")
        with self.assertRaises(stage.ReleasePrepError):
            stage.next_tag("V3.2.9999", "V3.2.9999")


if __name__ == "__main__":
    unittest.main()
