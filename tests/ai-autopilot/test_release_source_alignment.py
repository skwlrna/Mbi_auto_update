"""V3.2.19 AI source: pinned revision, fail closed, no overwrite of prior history."""
import subprocess
import sys
import tempfile
from pathlib import Path
from unittest import TestCase, main, mock

AI = Path(__file__).resolve().parents[2] / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AI))
import release_source


class PinnedSourceTests(TestCase):
    def init_repo(self, root, marker):
        upstream = root / "origin"
        upstream.mkdir()
        def git(args, path):
            return subprocess.run(["git", *args], cwd=path, check=True,
                                  capture_output=True, text=True, timeout=40)
        git(["init"], upstream)
        git(["checkout", "-b", "main"], upstream)
        git(["config", "user.name", "Test"], upstream)
        git(["config", "user.email", "test@example.invalid"], upstream)
        f = upstream / "source/FishingAutomation/FishingAutomation.csproj"
        f.parent.mkdir(parents=True)
        f.write_text("<Project />")
        git(["add", "."], upstream)
        git(["commit", "-m", "old"], upstream)
        old_sha = git(["rev-parse", "HEAD"], upstream).stdout.strip()
        f.write_text("<Project><PropertyGroup/></Project>")
        git(["add", "."], upstream)
        git(["commit", "-m", "released"], upstream)
        sha = git(["rev-parse", "HEAD"], upstream).stdout.strip()
        marker.write_text(sha + "\n")
        return upstream, old_sha, sha, git

    @mock.patch.object(release_source, "TRUSTED_ORIGINS", new_callable=lambda: set())
    def test_old_repo_is_preserved_and_clean_release_snapshot_created(self, trusted):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            marker = root / "BUILD_COMMIT.txt"
            upstream, old_sha, expected, git = self.init_repo(root, marker)
            trusted.add(str(upstream))
            release_source.SOURCE_URL = str(upstream)
            self.addCleanup(setattr, release_source, "SOURCE_URL",
                            "https://github.com/skwlrna/Mbi_auto_update.git")
            old = root / "repo"
            git(["clone", str(upstream), str(old)], root)
            git(["checkout", old_sha], old)
            snapshots = root / "snapshots"
            c = {"repo": str(old), "source_snapshots_dir": str(snapshots)}
            chosen = release_source.prepare_source(c, marker)
            self.assertEqual(chosen["expected"], expected)
            self.assertNotEqual(Path(chosen["repo"]), old)
            self.assertEqual(git(["rev-parse", "HEAD"], old).stdout.strip(), old_sha)
            self.assertEqual(git(["rev-parse", "HEAD"], Path(chosen["repo"])).stdout.strip(), expected)
            self.assertEqual(release_source.prepare_source(c, marker)["action"],
                             "existing_pinned_snapshot_selected")
            # A compromised snapshot is not deleted or silently repaired.
            (Path(chosen["repo"]) / "source/FishingAutomation/FishingAutomation.csproj").write_text("dirty")
            with self.assertRaises(release_source.SourceBlocked):
                release_source.prepare_source(c, marker)

    def test_missing_or_invalid_release_marker_blocks_all_source_selection(self):
        with tempfile.TemporaryDirectory() as temp:
            marker = Path(temp) / "BUILD_COMMIT.txt"
            c = {"repo": str(Path(temp) / "repo"),
                 "source_snapshots_dir": str(Path(temp) / "snapshots")}
            with self.assertRaises(release_source.SourceBlocked):
                release_source.prepare_source(c, marker)
            marker.write_text("garbage", encoding="ascii")
            with self.assertRaises(release_source.SourceBlocked):
                release_source.prepare_source(c, marker)
            self.assertFalse((Path(temp) / "snapshots").exists())
