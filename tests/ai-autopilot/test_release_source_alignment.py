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

    @mock.patch.object(release_source, "TRUSTED_ORIGINS", new_callable=lambda: set())
    def test_exact_unreleased_candidate_sha_is_fetched_not_main_tip(self, trusted):
        # Regression for a real V3.2.46 RC: BUILD_COMMIT points at a PR
        # branch commit, while trusted GitHub main still runs V3.2.40.
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            marker = root / "BUILD_COMMIT.txt"
            upstream, _, main_sha, git = self.init_repo(root, marker)
            git(["checkout", "-b", "release/v3246-staged-auto-update"], upstream)
            file = upstream / "source/FishingAutomation/FishingAutomation.csproj"
            file.write_text("<Project><PropertyGroup><Version>3.2.46</Version></PropertyGroup></Project>")
            git(["add", "."], upstream)
            git(["commit", "-m", "rc candidate"], upstream)
            candidate = git(["rev-parse", "HEAD"], upstream).stdout.strip()
            git(["checkout", "main"], upstream)
            marker.write_text(candidate + "\n", encoding="ascii")

            trusted.add(str(upstream))
            release_source.SOURCE_URL = str(upstream)
            self.addCleanup(setattr, release_source, "SOURCE_URL",
                            "https://github.com/skwlrna/Mbi_auto_update.git")
            original = root / "old-repo"
            git(["clone", str(upstream), str(original)], root)
            snapshots = root / "snapshots"
            chosen = release_source.prepare_source({
                "repo": str(original),
                "source_snapshots_dir": str(snapshots)
            }, marker)
            self.assertEqual(chosen["expected"], candidate)
            self.assertTrue(release_source.check_checkout(Path(chosen["repo"]), candidate)[0])
            self.assertEqual(git(["rev-parse", "HEAD"], original).stdout.strip(), main_sha)
            self.assertEqual(git(["rev-parse", "HEAD"], upstream).stdout.strip(), main_sha)
            self.assertEqual(release_source.prepare_source({
                "repo": str(original), "source_snapshots_dir": str(snapshots)
            }, marker)["action"], "existing_pinned_snapshot_selected")

    @mock.patch.object(release_source, "TRUSTED_ORIGINS", new_callable=lambda: set())
    def test_exact_release_is_selected_even_when_current_main_lacks_source(self, trusted):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            marker = root / "BUILD_COMMIT.txt"
            upstream, _, expected, git = self.init_repo(root, marker)
            trusted.add(str(upstream))
            with mock.patch.object(release_source, "SOURCE_URL", str(upstream)):
                project = upstream / "source/FishingAutomation/FishingAutomation.csproj"
                project.unlink()
                git(["add", "-u"], upstream)
                git(["commit", "-m", "main removed project"], upstream)
                chosen = release_source.prepare_source({
                    "repo": str(root / "no-local-repo"),
                    "source_snapshots_dir": str(root / "snapshots"),
                }, marker)
                self.assertEqual(chosen["expected"], expected)
                self.assertTrue(release_source.check_checkout(
                    Path(chosen["repo"]), expected)[0])
                self.assertTrue((Path(chosen["repo"]) /
                                 "source/FishingAutomation/FishingAutomation.csproj").exists())

    @mock.patch.object(release_source, "TRUSTED_ORIGINS", new_callable=lambda: set())
    def test_exact_pr_head_fallback_uses_matching_sha_not_hardcoded_pr(self, trusted):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            marker = root / "BUILD_COMMIT.txt"
            upstream, _, main_sha, git = self.init_repo(root, marker)
            git(["checkout", "-b", "release/candidate"], upstream)
            project = upstream / "source/FishingAutomation/FishingAutomation.csproj"
            project.write_text("<Project><PropertyGroup><Version>3.2.50</Version></PropertyGroup></Project>")
            git(["add", "."], upstream)
            git(["commit", "-m", "isolated RC"], upstream)
            expected = git(["rev-parse", "HEAD"], upstream).stdout.strip()
            git(["update-ref", "refs/pull/432/head", expected], upstream)
            git(["update-ref", "refs/pull/255/head", main_sha], upstream)
            git(["checkout", "main"], upstream)
            marker.write_text(expected + "\n", encoding="ascii")
            trusted.add(str(upstream))
            original = release_source.run_git
            commands = []
            def fail_direct_sha_fetch(args, cwd=None, timeout=30):
                commands.append(list(args))
                if (args[:4] == ["fetch", "--no-tags", "--depth", "1"]
                        and args[-1] == expected):
                    return subprocess.CompletedProcess(args, 1, "", "SHA fetch denied")
                return original(args, cwd, timeout)
            with mock.patch.object(release_source, "SOURCE_URL", str(upstream)), \
                 mock.patch.object(release_source, "run_git",
                                   side_effect=fail_direct_sha_fetch):
                chosen = release_source.prepare_source({
                    "repo": str(root / "missing"),
                    "source_snapshots_dir": str(root / "snapshots"),
                }, marker)
            self.assertEqual(chosen["expected"], expected)
            self.assertTrue(release_source.check_checkout(Path(chosen["repo"]), expected)[0])
            self.assertTrue(any(cmd[-1] == "refs/pull/432/head" for cmd in commands))
            self.assertFalse(any(cmd[-1] == "refs/pull/255/head" for cmd in commands))

    @mock.patch.object(release_source, "TRUSTED_ORIGINS", new_callable=lambda: set())
    def test_direct_sha_denied_uses_bounded_history_for_older_main_release(self, trusted):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            marker = root / "BUILD_COMMIT.txt"
            upstream, _, expected, git = self.init_repo(root, marker)
            file = upstream / "source/FishingAutomation/FishingAutomation.csproj"
            file.write_text("<Project><PropertyGroup>new main</PropertyGroup></Project>")
            git(["add", "."], upstream)
            git(["commit", "-m", "later main"], upstream)
            trusted.add(str(upstream))
            commands = []
            run_git_original = release_source.run_git
            def deny_direct(args, cwd=None, timeout=30):
                commands.append(list(args))
                if args[:2] == ["fetch", "--no-tags"] and args[-1] == expected:
                    return subprocess.CompletedProcess(args, 1, "", "not allowed")
                return run_git_original(args, cwd, timeout)
            with mock.patch.object(release_source, "SOURCE_URL", str(upstream)), \
                 mock.patch.object(release_source, "run_git", side_effect=deny_direct):
                result = release_source.prepare_source({
                    "repo": str(root / "missing"),
                    "source_snapshots_dir": str(root / "snapshots")
                }, marker)
            self.assertEqual(result["expected"], expected)
            self.assertTrue(release_source.check_checkout(Path(result["repo"]), expected)[0])
            self.assertTrue(any(cmd[-2:] == ["origin", "refs/heads/main"] and
                                "256" in cmd for cmd in commands))

    @mock.patch.object(release_source, "TRUSTED_ORIGINS", new_callable=lambda: set())
    def test_previous_rc_sha_inside_legacy_pr_history_still_resolves(self, trusted):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            marker = root / "BUILD_COMMIT.txt"
            upstream, _, _, git = self.init_repo(root, marker)
            git(["checkout", "-b", "old-rc"], upstream)
            file = upstream / "source/FishingAutomation/FishingAutomation.csproj"
            file.write_text("<Project><PropertyGroup>candidate</PropertyGroup></Project>")
            git(["add", "."], upstream)
            git(["commit", "-m", "pinned candidate"], upstream)
            expected = git(["rev-parse", "HEAD"], upstream).stdout.strip()
            file.write_text("<Project><PropertyGroup>new rc head</PropertyGroup></Project>")
            git(["add", "."], upstream)
            git(["commit", "-m", "later rc head"], upstream)
            head = git(["rev-parse", "HEAD"], upstream).stdout.strip()
            git(["update-ref", "refs/pull/255/head", head], upstream)
            git(["checkout", "main"], upstream)
            marker.write_text(expected + "\n", encoding="ascii")
            trusted.add(str(upstream))
            called = []
            run_git_original = release_source.run_git
            def deny_direct(args, cwd=None, timeout=30):
                called.append(list(args))
                if args[:2] == ["fetch", "--no-tags"] and args[-1] == expected:
                    return subprocess.CompletedProcess(args, 1, "", "not allowed")
                return run_git_original(args, cwd, timeout)
            with mock.patch.object(release_source, "SOURCE_URL", str(upstream)), \
                 mock.patch.object(release_source, "run_git", side_effect=deny_direct):
                result = release_source.prepare_source({
                    "repo": str(root / "missing"),
                    "source_snapshots_dir": str(root / "snapshots")
                }, marker)
            self.assertEqual(result["expected"], expected)
            self.assertTrue(release_source.check_checkout(Path(result["repo"]), expected)[0])
            self.assertTrue(any(cmd[-1] == "refs/pull/255/head" for cmd in called))


    def test_ui_explains_local_source_validation_without_false_github_absence(self):
        app = AI.parent
        host = (app / "AiAutopilotHost.cs").read_text(encoding="utf-8")
        dashboard = (app / "AiAutopilotDashboard.cs").read_text(encoding="utf-8")
        self.assertIn("release_BUILD_COMMIT", host)
        self.assertIn("existing_snapshot_not_trusted", host)
        self.assertIn("pinned_release_commit_unavailable", host)
        self.assertIn("GitHub 소스 부재가 아님", host)
        self.assertIn("release_source_guard", dashboard)
        self.assertIn("GitHub 소스 부재 아님", dashboard)

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
