"""Opt-in, fail-closed retention maintenance for isolated AI worktrees.

Never remove a worktree automatically. A human-approved cleanup archives a
terminal, unchanged worktree, verifies its backup, and only then removes it.
Results, evidence, Git branches and original source snapshots are untouched.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import re
import subprocess
import zipfile
from release_source import check_checkout, release_commit, SourceBlocked, TRUSTED_ORIGINS

IDENTIFIER = re.compile(r"[0-9a-f]{16}\Z")
TERMINAL = {"blocked", "needs_review", "tests_failed"}
ARCHIVE_LIMIT_BYTES = 512 * 1024 * 1024


def _git(repo: Path, *args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(["git", "-C", str(repo), *args], capture_output=True,
                          text=True, encoding="utf-8", errors="replace",
                          timeout=30, shell=False)


def _repo(c: dict) -> Path:
    # The running watcher may have selected a release-pinned snapshot while
    # the configured /repo remains on an older commit.
    old = Path(c["repo"]).expanduser()
    try:
        expected = release_commit()
    except (OSError, SourceBlocked):
        return old
    root = Path(c["results_dir"]).expanduser()
    record = root / "source-alignment.json"
    if not record.is_file() or record.is_symlink() or record.stat().st_size > 4096:
        return old
    try:
        data = json.loads(record.read_text(encoding="utf-8"))
        source = Path(data.get("selected_repo", "")).expanduser()
        if (data.get("status") == "verified"
                and data.get("expected_release_sha") == expected
                and check_checkout(source, expected)[0]):
            return source
    except (OSError, ValueError, TypeError):
        pass
    return old


def _registrations(c: dict) -> dict[Path, Path]:
    """Resolve the owning *trusted* Git root, including legacy pre-snapshot runs.

    The pinned release snapshot and old configured repo are separate Git
    repositories. A worktree must be removed using the repo that registered it.
    Nothing here rewrites or pulls either repository.
    """
    old_default = Path.home() / "MabiAutoAI" / "repo"
    choices = (_repo(c), Path(c.get("original_repo", old_default)).expanduser(),
               old_default)
    owners: dict[Path, Path] = {}
    visited: set[Path] = set()
    for candidate in choices:
        try:
            if candidate.is_symlink() or not candidate.is_dir():
                continue
            resolved = candidate.resolve()
            if resolved in visited:
                continue
            visited.add(resolved)
            top = _git(resolved, "rev-parse", "--show-toplevel")
            remote = _git(resolved, "remote", "get-url", "origin")
            if (top.returncode or remote.returncode
                    or Path(top.stdout.strip()).resolve() != resolved
                    or remote.stdout.strip() not in TRUSTED_ORIGINS):
                continue
            listed = _git(resolved, "worktree", "list", "--porcelain")
            if listed.returncode:
                continue
            for line in listed.stdout.splitlines():
                if line.startswith("worktree "):
                    owners[Path(line[9:]).resolve()] = resolved
        except (OSError, subprocess.TimeoutExpired, ValueError):
            continue
    return owners


def _existing_report(c: dict, event_id: str) -> dict:
    path = Path(c["results_dir"]).expanduser() / event_id / "result.json"
    if path.is_symlink() or not path.is_file() or path.stat().st_size > 131072:
        return {}
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        return data if isinstance(data, dict) else {}
    except (OSError, ValueError, UnicodeError):
        return {}


def _eligible(folder: Path, c: dict, registered: dict[Path, Path]) -> bool:
    # A missing/foreign worktree, an active job, or a worktree with edits cannot
    # be cleaned automatically or by the opt-in clean-only action.
    if (not IDENTIFIER.fullmatch(folder.name) or folder.is_symlink()
            or folder.resolve() not in registered):
        return False
    result = _existing_report(c, folder.name)
    if (result.get("event_id") != folder.name
            or result.get("status") not in TERMINAL
            or result.get("changed_files") or result.get("pr")
            or result.get("worktree") != str(folder)):
        return False
    try:
        status = _git(folder, "status", "--porcelain", "--untracked-files=all")
        return status.returncode == 0 and not status.stdout.strip()
    except (OSError, subprocess.TimeoutExpired):
        return False


def report(c: dict) -> dict:
    root = Path(c["worktrees_dir"]).expanduser()
    limit = int(c.get("max_worktrees", 30))
    data = {"kind": "retention_read_only", "limit": limit,
            "count": 0, "clean_candidates": [], "protected_count": 0,
            "warning": False}
    if root.is_symlink():
        data["error"] = "worktree_root_symlink"
        return data
    if not root.is_dir():
        return data
    try:
        registered = _registrations(c)
        folders = sorted(p for p in root.iterdir() if p.is_dir())
        data["count"] = len(folders)
        for folder in folders:
            if _eligible(folder, c, registered):
                data["clean_candidates"].append(folder.name)
        data["protected_count"] = len(folders) - len(data["clean_candidates"])
        data["warning"] = len(folders) >= max(0, limit - 3)
        if folders and not registered:
            data["error"] = "no_trusted_git_worktree_registrations"
    except (OSError, subprocess.TimeoutExpired, ValueError):
        data["error"] = "retention_inventory_unavailable"
    return data


def archive_clean_one(c: dict, event_id: str) -> dict:
    """Called only after explicit UI approval, while watcher lock is held."""
    if not IDENTIFIER.fullmatch(event_id):
        return {"status": "blocked", "reason": "invalid_event_id"}
    snapshot = report(c)
    if snapshot.get("error") or event_id not in snapshot["clean_candidates"]:
        return {"status": "blocked", "reason": "not_verified_clean_terminal"}
    root = Path(c["worktrees_dir"]).expanduser()
    folder = root / event_id
    archive_root = Path(c.get("worktree_archives_dir",
                              str(Path.home() / "MabiAutoAI" / "worktree-archives"))).expanduser()
    if archive_root.is_symlink():
        return {"status": "blocked", "reason": "archive_root_symlink"}
    archive_root.mkdir(parents=True, exist_ok=True)
    final = archive_root / (event_id + ".zip")
    staging = archive_root / (event_id + ".zip.partial")
    if final.exists() or staging.exists():
        return {"status": "blocked", "reason": "archive_already_exists"}
    checksums: dict[str, str] = {}
    total = 0
    try:
        with zipfile.ZipFile(staging, "x", compression=zipfile.ZIP_DEFLATED,
                             allowZip64=True) as archive:
            for entry in folder.rglob("*"):
                if entry.is_symlink():
                    raise ValueError("linked_file_blocked")
                if not entry.is_file():
                    continue
                relative = entry.relative_to(folder).as_posix()
                total += entry.stat().st_size
                if total > ARCHIVE_LIMIT_BYTES:
                    raise ValueError("archive_size_limit")
                raw = entry.read_bytes()
                checksums[relative] = hashlib.sha256(raw).hexdigest()
                archive.writestr(relative, raw)
        with zipfile.ZipFile(staging, "r") as check:
            if set(check.namelist()) != set(checksums):
                raise ValueError("archive_entries_mismatch")
            for name, digest in checksums.items():
                if hashlib.sha256(check.read(name)).hexdigest() != digest:
                    raise ValueError("archive_checksum_mismatch")
        # Recheck immediately before destructive Git action; never force
        # through tracked/untracked edits or an unavailable registration.
        if event_id not in report(c).get("clean_candidates", []):
            raise ValueError("worktree_changed_during_backup")
        staging.rename(final)
        owner = _registrations(c).get(folder.resolve())
        if owner is None:
            return {"status": "blocked", "reason": "registered_owner_disappeared"}
        removed = _git(owner, "worktree", "remove", "--force", str(folder))
        if removed.returncode:
            return {"status": "blocked", "reason": "git_worktree_remove_failed_backup_kept"}
        return {"status": "archived", "reason": "verified_backup_then_clean_worktree_removed",
                "event_id": event_id}
    except (OSError, ValueError, subprocess.TimeoutExpired, zipfile.BadZipFile) as exc:
        return {"status": "blocked", "reason": type(exc).__name__}
    finally:
        # Never remove a finalized backup, an original result, or error evidence.
        if staging.exists():
            staging.unlink()
