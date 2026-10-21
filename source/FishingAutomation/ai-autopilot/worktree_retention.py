"""Fail-closed retention maintenance for isolated AI worktrees.

An agent_no_patch worktree can be discarded immediately only after confirming
it has no tracked edits or unknown on-disk files. It needs no full-source ZIP;
diagnostics live outside the worktree. Other terminal clean worktrees still use
verified archives. Active worktrees and actual patches remain protected.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import re
import subprocess
import zipfile
from release_source import check_checkout, release_commit, SourceBlocked, TRUSTED_ORIGINS

IDENTIFIER = re.compile(r"[0-9a-f]{16}(?:-retry-retention-v1|-retry-cli-arg-v121)?\Z")
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


def _generated_only(folder: Path) -> bool:
    """Only untracked bin/obj build output may be forcibly removed *after*
    a checksum-verified full-worktree archive of an agent_no_patch result.

    A tracked edit, rename, unknown untracked file, symlink or unreadable
    status must keep the worktree protected. Never use git clean.
    """
    status = _git(folder, "status", "--porcelain=v1", "-z", "--untracked-files=all")
    if status.returncode or not status.stdout:
        return False
    for entry in status.stdout.split("\0"):
        if not entry:
            continue
        if not entry.startswith("?? "):
            return False
        relative = entry[3:]
        parts = relative.split("/")
        if (len(parts) < 2 or any(not p or p in (".", "..") for p in parts)
                or not any(p in ("bin", "obj") for p in parts[:-1])):
            return False
        candidate = folder.joinpath(*parts)
        if (candidate.is_symlink() or not candidate.is_file()
                or not candidate.resolve().is_relative_to(folder.resolve())):
            return False
    return True


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
        if status.returncode:
            return False
        if not status.stdout.strip():
            return True
        # A no-patch Codex attempt can leave NuGet/bin/obj output behind.
        # Back it up, but NEVER treat source edits as disposable artifacts.
        return (result.get("status") == "needs_review"
                and str(result.get("reason", "")).startswith("agent_no_patch")
                and _generated_only(folder))
    except (OSError, subprocess.TimeoutExpired):
        return False


def report(c: dict) -> dict:
    root = Path(c["worktrees_dir"]).expanduser()
    limit = int(c.get("max_worktrees", 30))
    data = {"kind": "retention_read_only", "limit": limit,
            "count": 0, "clean_candidates": [], "archive_ready_candidates": [],
            "archive_conflicts": [], "protected_count": 0, "warning": False}
    if root.is_symlink():
        data["error"] = "worktree_root_symlink"
        return data
    if not root.is_dir():
        return data
    archive_root = Path(c.get("worktree_archives_dir",
                              str(Path.home() / "MabiAutoAI" / "worktree-archives"))).expanduser()
    if archive_root.is_symlink():
        data["error"] = "archive_root_symlink"
        return data
    try:
        registered = _registrations(c)
        folders = sorted(p for p in root.iterdir() if p.is_dir())
        data["count"] = len(folders)
        for folder in folders:
            if _eligible(folder, c, registered):
                data["clean_candidates"].append(folder.name)
                # A pre-existing ZIP or an abandoned partial backup must not
                # monopolize the one-at-a-time cleanup button. Never overwrite it.
                archive_files = (archive_root / (folder.name + ".zip"),
                                 archive_root / (folder.name + ".zip.partial"))
                existing = _existing_report(c, folder.name)
                disposable_no_patch = (existing.get("status") == "needs_review"
                    and str(existing.get("reason", "")).startswith("agent_no_patch"))
                # A prior ZIP cannot block a no-patch checkout: the source
                # checkout will not be archived or modify that existing ZIP.
                if (not disposable_no_patch
                        and any(p.exists() or p.is_symlink() for p in archive_files)):
                    data["archive_conflicts"].append(folder.name)
                else:
                    data["archive_ready_candidates"].append(folder.name)
        data["protected_count"] = len(folders) - len(data["clean_candidates"])
        data["warning"] = len(folders) >= max(0, limit - 3)
        if folders and not registered:
            data["error"] = "no_trusted_git_worktree_registrations"
    except (OSError, subprocess.TimeoutExpired, ValueError):
        data["error"] = "retention_inventory_unavailable"
    return data


def _discard_no_patch_one(c: dict, event_id: str, folder: Path,
                          registered: dict[Path, Path]) -> dict:
    """Drop only a proven no-source-change Codex checkout without a full ZIP.

    The original incident, redacted Codex logs and result.json live outside
    this disposable checkout. Retain any unexpected file or source change.
    """
    original = _existing_report(c, event_id)
    if (original.get("event_id") != event_id
            or original.get("status") != "needs_review"
            or not str(original.get("reason", "")).startswith("agent_no_patch")
            or original.get("changed_files") or original.get("pr")
            or original.get("worktree") != str(folder)
            or folder.is_symlink() or folder.resolve() not in registered):
        return {"status": "blocked", "reason": "no_patch_discard_not_authorized"}
    # Check BOTH index and working tree. An unchanged git index alone is not
    # sufficient if an untracked or ignored non-build file was created.
    difference = _git(folder, "diff", "--quiet", "HEAD", "--")
    if difference.returncode:
        return {"status": "blocked", "reason": "tracked_source_changed"}
    tracked = _git(folder, "ls-files", "-z", "--cached")
    if tracked.returncode:
        return {"status": "blocked", "reason": "tracked_files_unavailable"}
    known = set(filter(None, tracked.stdout.split("\0")))
    try:
        for entry in folder.rglob("*"):
            if entry.is_symlink():
                return {"status": "blocked", "reason": "worktree_symlink_detected"}
            if not entry.is_file():
                continue
            relative = entry.relative_to(folder).as_posix()
            if relative == ".git" or relative in known:
                continue
            parts = relative.split("/")
            if not any(part in ("bin", "obj") for part in parts[:-1]):
                return {"status": "blocked", "reason": "unexpected_untracked_file"}
    except (OSError, ValueError):
        return {"status": "blocked", "reason": "worktree_inventory_unavailable"}
    # Repeat the authorization and source-diff checks as late as possible.
    if (not _eligible(folder, c, registered)
            or _git(folder, "diff", "--quiet", "HEAD", "--").returncode):
        return {"status": "blocked", "reason": "worktree_changed_before_discard"}
    owner = registered.get(folder.resolve())
    if owner is None:
        return {"status": "blocked", "reason": "registered_owner_disappeared"}
    # --force is safe only after the complete checkout file inventory above;
    # Git rejects non-forced removal when ignored bin/obj output remains.
    removed = _git(owner, "worktree", "remove", "--force", str(folder))
    if removed.returncode:
        return {"status": "blocked", "reason": "git_worktree_remove_failed"}
    return {"status": "cleaned", "reason": "verified_no_patch_no_zip_needed",
            "event_id": event_id}


def archive_clean_one(c: dict, event_id: str) -> dict:
    """Called by the authorized watcher for approved or policy-bounded cleanup.

    The watcher singleton lock guards cleanup; report() revalidates every candidate.
    """
    if not IDENTIFIER.fullmatch(event_id):
        return {"status": "blocked", "reason": "invalid_event_id"}
    snapshot = report(c)
    if snapshot.get("error") or event_id not in snapshot["archive_ready_candidates"]:
        return {"status": "blocked", "reason": "not_verified_clean_terminal_or_archive_conflict"}
    root = Path(c["worktrees_dir"]).expanduser()
    folder = root / event_id
    # No-patch analysis must NOT consume an archival ZIP or a worktree slot.
    # Return with a clear blocking reason if the checkout is not disposable.
    existing = _existing_report(c, event_id)
    if (existing.get("status") == "needs_review"
            and str(existing.get("reason", "")).startswith("agent_no_patch")):
        return _discard_no_patch_one(c, event_id, folder, _registrations(c))
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
        # Recheck every current byte (including ignored files) against the
        # archive before removing anything, not merely git's tracked status.
        current: dict[str, str] = {}
        for entry in folder.rglob("*"):
            if entry.is_symlink():
                raise ValueError("linked_file_blocked")
            if entry.is_file():
                current[entry.relative_to(folder).as_posix()] = hashlib.sha256(
                    entry.read_bytes()).hexdigest()
        if current != checksums:
            raise ValueError("worktree_changed_during_backup")
        if event_id not in report(c).get("clean_candidates", []):
            raise ValueError("worktree_changed_during_backup")
        status = _git(folder, "status", "--porcelain", "--untracked-files=all")
        if status.returncode:
            raise ValueError("git_status_unavailable_before_remove")
        generated = bool(status.stdout.strip())
        if generated and not _generated_only(folder):
            raise ValueError("untrusted_worktree_change_before_remove")
        staging.rename(final)
        owner = _registrations(c).get(folder.resolve())
        if owner is None:
            return {"status": "blocked", "reason": "registered_owner_disappeared"}
        # Force is *only* allowed for known untracked build artifacts after
        # verified archival. Normal clean worktrees use the non-force route.
        args = ("worktree", "remove", "--force", str(folder)) if generated else (
            "worktree", "remove", str(folder))
        removed = _git(owner, *args)
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
