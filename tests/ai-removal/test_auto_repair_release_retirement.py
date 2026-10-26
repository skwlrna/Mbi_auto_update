"""Retirement release must not re-enable standalone repair, break error uploads, or silently skip local cleanup."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]

class RetirementReleaseContract(unittest.TestCase):
    def test_dedicated_auto_repair_deleted(self):
        for p in ("tools/mabi-auto-repair", ".github/workflows/mabi-auto-repair-ci.yml",
                  ".github/workflows/auto-promote-ci-repair.yml",
                  ".github/workflows/prepare-reviewed-repair-release.yml",
                  ".github/workflows/publish-reviewed-repair-auto-update.yml"):
            self.assertFalse((ROOT/p).exists(), p)

    def test_startup_and_cleanup(self):
        app = (ROOT/"source/FishingAutomation/MainForm.cs").read_text(encoding="utf-8")
        mgr = (ROOT/"source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8")
        cleanup = (ROOT/"source/tools/RetireAutoRepair.ps1").read_text(encoding="utf-8")
        self.assertIn("approvedUpdateStartup", app)
        self.assertIn("UpdateManager.LaunchAutoRepairRetirement();", app)
        self.assertIn("public static void LaunchAutoRepairRetirement()", mgr)
        self.assertIn("RetireAutoRepair.ps1", mgr)
        for token in ("MabiAutoRepair-Watch", ".MabiAutoRepair", "MabiAutoAI",
                      "schtasks.exe", "Stop-Process", "ReparsePoint"):
            self.assertIn(token, cleanup)
        self.assertNotIn("Mbi_auto_log", cleanup)
        self.assertNotIn("FishingAutomation.exe", cleanup)

    def test_release_package_and_versions(self):
        release = (ROOT/".github/workflows/release-v3264-auto-update.yml").read_text(encoding="utf-8")
        self.assertIn("Copy-Item source/tools/RetireAutoRepair.ps1", release)
        self.assertIn("tools/RetireAutoRepair.ps1", release)
        self.assertIn("source/V3.2.64_AUTO_REPAIR_RETIREMENT.json", release)
        self.assertIn("MabiAuto_V3.2.64_Windows_Lite.zip.sha256", release)
        # This is a V3.2.64 historical retirement contract. Later releases
        # must retain the historical workflow but may advance their version.
        import re
        mgr = (ROOT/"source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8")
        csproj = (ROOT/"source/FishingAutomation/FishingAutomation.csproj").read_text(encoding="utf-8")
        match = re.search(r'CurrentVersion = "V(\d+\.\d+\.\d+)"', updater)
        self.assertIsNotNone(match)
        version = tuple(int(part) for part in match.groups())
        self.assertGreaterEqual(version, (3, 2, 64))
        self.assertIn("<Version>%s</Version>" % ".".join(map(str, version)), csproj)

    def test_newer_release_workflow_matches_version_literal(self):
        # Every new public release must provide an exact updater/version
        # workflow. A prior regression silently skipped V3.2.66 because
        # the updater version check was stale.
        import re
        updater = (ROOT/"source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8")
        project = (ROOT/"source/FishingAutomation/FishingAutomation.csproj").read_text(encoding="utf-8")
        match = re.search(r'CurrentVersion = "V(\\d+\\.\\d+\\.\\d+)"', updater)
        self.assertIsNotNone(match)
        version = match.group(1)
        self.assertGreaterEqual(tuple(int(x) for x in version.split(".")), (3, 2, 66))
        release_path = ROOT/(".github/workflows/release-v" + version.replace(".", "") + "-auto-update.yml")
        self.assertTrue(release_path.exists(), release_path)
        workflow = release_path.read_text(encoding="utf-8")
        expected = 'CurrentVersion = "V' + version + '"'
        self.assertIn(expected, updater)
        self.assertIn("<Version>" + version + "</Version>", project)
        self.assertIn("if(-not $u.Contains('" + expected + "'))", workflow)
        self.assertIn("MabiAuto_V" + version + "_Windows_Lite.zip.sha256", workflow)
        # Retain the historical V3.2.66 guard and assets without pinning
        # the running updater to that old release forever.
        prior = (ROOT/".github/workflows/release-v3266-auto-update.yml").read_text(encoding="utf-8")
        self.assertIn('CurrentVersion = "V3.2.66"', prior)
        self.assertIn("MabiAuto_V3.2.66_Windows_Lite.zip.sha256", prior)

if __name__ == "__main__":
    unittest.main()
