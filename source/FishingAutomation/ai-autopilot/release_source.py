"""Pinned release-source preparation for the local, opt-in Codex watcher.

Only the installed package's BUILD_COMMIT.txt may select the source revision.
Never update the user's original repo, previous worktrees, results or errors.
The exact installed source SHA is fetched from the fixed official GitHub origin
into a temporary directory and published atomically,
or analysis remains blocked. No checkout is allowed from an untrusted origin.
"""
from __future__ import annotations
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

SOURCE_URL = "https://github.com/skwlrna/Mbi_auto_update.git"
TRUSTED_ORIGINS = frozenset({
    SOURCE_URL,
    "https://github.com/skwlrna/Mbi_auto_update",
    "git@github.com:skwlrna/Mbi_auto_update",
    "git@github.com:skwlrna/Mbi_auto_update.git",
    "ssh://git@github.com/skwlrna/Mbi_auto_update.git",
})
# In a release archive: <package>/release/ai-autopilot/release_source.py
RELEASE_MARKER = Path(__file__).resolve().parents[2] / "BUILD_COMMIT.txt"


class SourceBlocked(RuntimeError):
    """A missing/mismatched source is NOT an authorization to edit or retry."""


def release_commit(marker: Path = RELEASE_MARKER) -> str:
    if marker.is_symlink() or not marker.is_file() or marker.stat().st_size > 128:
        raise SourceBlocked("release_BUILD_COMMIT_missing_or_untrusted")
    sha = marker.read_text(encoding="ascii").strip().lower()
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise SourceBlocked("release_BUILD_COMMIT_invalid")
    return sha


def run_git(args: list[str], cwd: Path | None = None, timeout: int = 30):
    return subprocess.run(["git", *args], cwd=str(cwd) if cwd else None,
                          capture_output=True, text=True, encoding="utf-8",
                          errors="replace", shell=False, timeout=timeout)


def check_checkout(repo: Path, expected: str) -> tuple[bool, str]:
    if not re.fullmatch(r"[0-9a-f]{40}", expected):
        return False, "invalid_release_sha"
    if repo.is_symlink() or not repo.is_dir():
        return False, "repo_missing_or_link"
    if not (repo / "source/FishingAutomation/FishingAutomation.csproj").is_file():
        return False, "missing_production_source"
    try:
        root = run_git(["rev-parse", "--show-toplevel"], repo)
        head = run_git(["rev-parse", "HEAD"], repo)
        origin = run_git(["remote", "get-url", "origin"], repo)
        clean = run_git(["status", "--porcelain", "--untracked-files=no"], repo)
        if any(v.returncode for v in (root, head, origin, clean)):
            return False, "git_inspection_failed"
        if Path(root.stdout.strip()).resolve() != repo.resolve():
            return False, "wrong_git_root"
        if origin.stdout.strip() not in TRUSTED_ORIGINS:
            return False, "untrusted_source_origin"
        if head.stdout.strip().lower() != expected:
            return False, "source_head_not_release"
        if clean.stdout.strip():
            return False, "tracked_files_dirty"
    except (OSError, subprocess.TimeoutExpired, ValueError):
        return False, "git_inspection_unavailable"
    return True, "release_source_verified"


def prepare_source(config: dict, marker: Path = RELEASE_MARKER) -> dict:
    """Called before watching/processing; may create a fresh immutable snapshot.

    Does NOT touch the original repo or any existing snapshot. A changed/dirty
    existing snapshot is blocked rather than overwritten.
    """
    expected = release_commit(marker)
    configured = Path(config["repo"]).expanduser().resolve()
    valid, reason = check_checkout(configured, expected)
    if valid:
        return {"repo": str(configured), "expected": expected,
                "action": "existing_release_source_verified"}
    root = Path(config.get("source_snapshots_dir",
                str(Path.home() / "MabiAutoAI" / "source-snapshots"))).expanduser()
    if root.is_symlink():
        raise SourceBlocked("snapshot_root_symlink_blocked")
    root.mkdir(parents=True, exist_ok=True)
    root = root.resolve()
    target = root / expected
    if target.exists() or target.is_symlink():
        valid, why = check_checkout(target, expected)
        if not valid:
            raise SourceBlocked("existing_snapshot_not_trusted:" + why)
        return {"repo": str(target), "expected": expected,
                "action": "existing_pinned_snapshot_selected",
                "previous_source_issue": reason}
    staging = Path(tempfile.mkdtemp(prefix=".building-release-", dir=root))
    try:
        # Always resolve the exact installed source SHA first. A release
        # candidate may not be present at the current main branch tip.
        # Never fetch from a user-configured or log-provided remote.
        if run_git(["init", "-q", str(staging)], timeout=40).returncode:
            raise SourceBlocked("pinned_snapshot_git_init_failed")
        if run_git(["remote", "add", "origin", SOURCE_URL], staging).returncode:
            raise SourceBlocked("pinned_snapshot_remote_setup_failed")

        failures: list[str] = []
        # The bounded main-history fallback helps when the installed release
        # was recently merged but GitHub disallows direct SHA fetch.
        for ref, depth in ((expected, 1), ("refs/heads/main", 1),
                           ("refs/heads/main", 256)):
            fetched = run_git(["fetch", "--no-tags", "--depth", str(depth),
                               "origin", ref], staging, timeout=180)
            if fetched.returncode:
                failures.append("exact_sha_fetch_failed" if ref == expected else
                                ("main_history_fetch_failed" if depth > 1
                                 else "main_fallback_fetch_failed"))
                continue
            # Main is only a way to obtain objects; never accept its HEAD
            # unless it matches the immutable installed commit exactly.
            detached = run_git(["-c", "advice.detachedHead=false",
                                "checkout", "--detach", expected], staging,
                               timeout=90)
            if detached.returncode:
                failures.append("pinned_commit_not_yet_reachable")
                continue
            valid, why = check_checkout(staging, expected)
            if not valid:
                raise SourceBlocked("pinned_release_source_not_verified:" + why)
            break
        else:
            # Direct SHA fetch can be denied by the server. Only use pull
            # request refs whose HEAD is the exact trusted BUILD_COMMIT SHA.
            # A hard-coded PR number would fail for newer release candidates.
            candidates: list[str] = []
            listed = run_git(["ls-remote", "origin", "refs/pull/*/head"],
                             staging, timeout=90)
            if listed.returncode == 0:
                for line in listed.stdout.splitlines():
                    pair = line.split()
                    if (len(pair) == 2 and pair[0].lower() == expected and
                            re.fullmatch(r"refs/pull/[1-9][0-9]*/head", pair[1])):
                        candidates.append(pair[1])
            if candidates:
                ref, depth = sorted(candidates)[0], 1
            else:
                # Preserve the older RC release path where the pinned commit
                # is an ancestor (not the HEAD) of the known historic PR.
                # This is strictly a fetch mechanism, not source authorization.
                ref, depth = "refs/pull/255/head", 100
            fetched = run_git(["fetch", "--no-tags", "--depth", str(depth),
                               "origin", ref], staging, timeout=180)
            if fetched.returncode:
                raise SourceBlocked("pinned_release_commit_unavailable:" +
                                    ",".join(failures))
            detached = run_git(["-c", "advice.detachedHead=false",
                                "checkout", "--detach", expected], staging,
                               timeout=90)
            if detached.returncode:
                raise SourceBlocked("pinned_release_commit_not_reachable")
            valid, why = check_checkout(staging, expected)
            if not valid:
                raise SourceBlocked("pinned_release_source_not_verified:" + why)
        # Atomic promotion; other watcher has an OS singleton lock.
        staging.rename(target)
        staging = None
        return {"repo": str(target), "expected": expected,
                "action": "fresh_pinned_snapshot_created",
                "previous_source_issue": reason}
    finally:
        if staging is not None and staging.exists():
            shutil.rmtree(staging)


def require_pinned_source(config: dict) -> str:
    expected = str(config.get("release_source_sha", ""))
    if not re.fullmatch(r"[0-9a-f]{40}", expected):
        raise SourceBlocked("release_source_guard_missing")
    if release_commit() != expected:
        raise SourceBlocked("installed_release_changed")
    ok, reason = check_checkout(Path(config["repo"]).expanduser().resolve(), expected)
    if not ok:
        raise SourceBlocked(reason)
    return expected
