"""Conservative, reversible recovery of terminal Codex worktrees.

Never touches the user's live macro or original error/result directories.
All worktree bytes (including modified and ignored files), available incident
evidence and result logs are backed up and SHA-256 verified before Git removes
a registered worktree. This is NOT a release or a claim of repair success.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import zipfile

from worktree_retention import (_registrations, _existing_report, _git, IDENTIFIER,
                                recorded_worktree_matches)

TERMINAL = frozenset({
    "blocked", "needs_review", "tests_failed", "tests_passed_review_required",
    "interrupted_review_required",
})
MAX_BYTES = 512 * 1024 * 1024
MAX_FILES = 60000


def _sources(c: dict, event_id: str, folder: Path) -> dict[str, Path]:
    roots = {"worktree": folder}
    results = Path(c["results_dir"]).expanduser() / event_id
    if results.is_dir() and not results.is_symlink():
        roots["result"] = results
    # Incident evidence is copied, never moved or purged.
    event = _existing_report(c, event_id).get("event", "")
    if isinstance(event, str) and re.fullmatch(r"[A-Za-z0-9_.-]{1,160}", event):
        for key in ("reports_dir", "runtime_reports_dir"):
            if key not in c:
                continue
            directory = Path(c[key]).expanduser() / event
            if directory.is_dir() and not directory.is_symlink():
                roots["evidence_" + key] = directory
    return roots


def _inventory(roots: dict[str, Path]) -> dict[str, tuple[Path, str, int]]:
    entries: dict[str, tuple[Path, str, int]] = {}
    total = 0
    for prefix, root in roots.items():
        if root.is_symlink() or not root.is_dir():
            raise ValueError("recovery_root_not_regular")
        for directory, dirs, files in os.walk(root, followlinks=False):
            parent = Path(directory)
            for name in dirs:
                if (parent / name).is_symlink():
                    raise ValueError("recovery_directory_symlink")
            for name in files:
                path = parent / name
                if path.is_symlink() or not path.is_file():
                    raise ValueError("recovery_file_not_regular")
                relative = path.relative_to(root).as_posix()
                if any(part in ("", ".", "..") for part in relative.split("/")):
                    raise ValueError("recovery_invalid_path")
                key = prefix + "/" + relative
                size = path.stat().st_size
                total += size
                if total > MAX_BYTES or len(entries) >= MAX_FILES:
                    raise ValueError("recovery_archive_limit")
                digest = hashlib.sha256()
                with path.open("rb") as handle:
                    for chunk in iter(lambda: handle.read(1024 * 1024), b""):
                        digest.update(chunk)
                entries[key] = (path, digest.hexdigest(), size)
    return entries


def _archive_matches(archive: Path, entries: dict, event_id: str) -> bool:
    if archive.is_symlink() or not archive.is_file():
        return False
    try:
        with zipfile.ZipFile(archive) as z:
            manifest = json.loads(z.read("RECOVERY-MANIFEST.json"))
            if (manifest.get("event_id") != event_id
                    or manifest.get("files") != {
                        name: {"sha256": sha, "size": size}
                        for name, (_, sha, size) in entries.items()}):
                return False
            if set(z.namelist()) != set(entries) | {"RECOVERY-MANIFEST.json"}:
                return False
            for name, (_, sha, size) in entries.items():
                digest = hashlib.sha256()
                observed = 0
                with z.open(name) as stream:
                    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                        observed += len(chunk)
                        digest.update(chunk)
                if observed != size or digest.hexdigest() != sha:
                    return False
        return True
    except (OSError, ValueError, KeyError, zipfile.BadZipFile):
        return False


def verify_saved_archive(archive: Path) -> dict:
    """Verify an existing recovery ZIP without accessing deleted worktrees.

    This is read-only: no extraction, Git changes, Codex or game access.
    """
    if archive.is_symlink() or not archive.is_file():
        return {"status": "blocked", "reason": "archive_missing_or_link"}
    try:
        if archive.stat().st_size > MAX_BYTES * 2:
            return {"status": "blocked", "reason": "archive_too_large"}
        with zipfile.ZipFile(archive) as z:
            records = z.infolist()
            names = [record.filename for record in records]
            if len(names) != len(set(names)) or "RECOVERY-MANIFEST.json" not in names:
                return {"status": "blocked", "reason": "duplicate_or_missing_entries"}
            manifest_info = z.getinfo("RECOVERY-MANIFEST.json")
            if (manifest_info.file_size > 10 * 1024 * 1024
                    or len(records) > MAX_FILES + 1):
                return {"status": "blocked", "reason": "manifest_too_large"}
            raw_manifest = z.read("RECOVERY-MANIFEST.json")
            manifest = json.loads(raw_manifest)
            event_id = manifest.get("event_id", "")
            files = manifest.get("files", {})
            if (manifest.get("schema") != 1 or not isinstance(event_id, str)
                    or not IDENTIFIER.fullmatch(event_id)
                    or not isinstance(files, dict)
                    or len(files) > MAX_FILES
                    or set(files) | {"RECOVERY-MANIFEST.json"} != set(names)):
                return {"status": "blocked", "reason": "manifest_invalid"}
            total = 0
            for name, item in files.items():
                # Ensure a saved ZIP can later be restored manually without
                # unsafe path traversal. No archive is extracted here.
                if not isinstance(name, str):
                    return {"status": "blocked", "reason": "manifest_path_or_digest_invalid"}
                pieces = name.split("/")
                if ("\\" in name
                        or len(pieces) < 2
                        or pieces[0] not in ("worktree", "result",
                                           "evidence_reports_dir",
                                           "evidence_runtime_reports_dir")
                        or any(part in ("", ".", "..") for part in pieces)
                        or not isinstance(item, dict)
                        or not isinstance(item.get("size"), int)
                        or not 0 <= item["size"] <= MAX_BYTES
                        or not isinstance(item.get("sha256"), str)
                        or not re.fullmatch(r"[0-9a-f]{64}", item["sha256"])):
                    return {"status": "blocked", "reason": "manifest_path_or_digest_invalid"}
                total += item["size"]
                if total > MAX_BYTES:
                    return {"status": "blocked", "reason": "uncompressed_size_limit"}
                digest = hashlib.sha256()
                observed = 0
                with z.open(name) as stream:
                    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                        observed += len(chunk)
                        if observed > item["size"]:
                            return {"status": "blocked", "reason": "entry_size_exceeded"}
                        digest.update(chunk)
                if observed != item["size"] or digest.hexdigest() != item["sha256"]:
                    return {"status": "blocked", "reason": "checksum_mismatch"}
        return {"status": "verified", "event_id": event_id,
                "file_count": len(files), "archive": str(archive)}
    except (OSError, ValueError, TypeError, KeyError, zipfile.BadZipFile, RuntimeError):
        return {"status": "blocked", "reason": "archive_read_or_integrity_error"}


def eligibility(c: dict, event_id: str, *, legacy: bool = False) -> tuple[str, str]:
    """Read-only explanation; no unknown/in-progress workspace is auto-reclaimed."""
    if not IDENTIFIER.fullmatch(event_id):
        return "protected", "invalid_worktree_identifier"
    root = Path(c["worktrees_dir"]).expanduser()
    folder = root / event_id
    if root.is_symlink() or folder.is_symlink() or not folder.is_dir():
        return "protected", "worktree_missing_or_link"
    try:
        owners = _registrations(c)
        if folder.resolve() not in owners:
            return "protected", "worktree_not_registered_with_trusted_repo"
        result = _existing_report(c, event_id)
        status = result.get("status")
        if status == "in_progress":
            return "protected", "in_progress_must_be_reviewed"
        if not legacy:
            if not result:
                return "protected", "result_record_missing_or_invalid"
            if result.get("event_id") != event_id:
                return "protected", "result_event_identity_mismatch"
            if not recorded_worktree_matches(result.get("worktree"), folder):
                return "protected", "result_worktree_path_mismatch"
            if status not in TERMINAL:
                return "protected", "result_status_requires_review"
        if legacy:
            # Legacy mode relaxes only MISSING metadata. It never accepts
            # conflicting ownership or a different recorded event identity.
            if result.get("event_id") not in (None, event_id):
                return "protected", "legacy_event_identity_conflict"
            if (result.get("worktree") and not recorded_worktree_matches(
                    result.get("worktree"), folder)):
                return "protected", "legacy_worktree_identity_conflict"
            if status not in TERMINAL and status not in (None, "", "interrupted"):
                return "protected", "active_or_unknown_status"
        head = _git(folder, "rev-parse", "HEAD")
        state = _git(folder, "status", "--porcelain=v1", "--untracked-files=all")
        if head.returncode or state.returncode or not re.fullmatch(
                r"[0-9a-fA-F]{40}", head.stdout.strip()):
            return "protected", "git_status_unavailable"
        return "recoverable", "terminal_with_backup_required"
    except (OSError, ValueError, subprocess.TimeoutExpired):
        return "protected", "worktree_inspection_failed"


def preview(c: dict, *, legacy: bool = False) -> dict:
    root = Path(c["worktrees_dir"]).expanduser()
    data = {"mode": "read_only", "recoverable": [], "protected": [],
            "count": 0, "warning": "No originals have been deleted."}
    if root.is_symlink():
        data["error"] = "worktree_root_symlink"
        return data
    if not root.is_dir():
        return data
    for folder in sorted(root.iterdir()):
        if folder.is_symlink() or not folder.is_dir():
            continue
        data["count"] += 1
        status, reason = eligibility(c, folder.name, legacy=legacy)
        data["recoverable" if status == "recoverable" else "protected"].append(
            {"event_id": folder.name, "reason": reason})
    return data


def recover_one(c: dict, event_id: str, *, legacy: bool = False) -> dict:
    status, reason = eligibility(c, event_id, legacy=legacy)
    if status != "recoverable":
        return {"status": "blocked", "reason": reason, "event_id": event_id}
    folder = Path(c["worktrees_dir"]).expanduser() / event_id
    owner = _registrations(c).get(folder.resolve())
    if owner is None:
        return {"status": "blocked", "reason": "registered_owner_disappeared"}
    root = Path(c.get("worktree_archives_dir",
                      str(Path.home() / "MabiAutoAI" / "worktree-archives"))).expanduser()
    if root.is_symlink():
        return {"status": "blocked", "reason": "archive_root_symlink"}
    final = root / ("recovery-" + event_id + ".zip")
    partial = root / ("recovery-" + event_id + ".zip.partial")
    try:
        root.mkdir(parents=True, exist_ok=True)
        if partial.exists() or partial.is_symlink():
            return {"status": "blocked", "reason": "partial_archive_requires_manual_review"}
        roots = _sources(c, event_id, folder)
        entries = _inventory(roots)
        if not entries or not any(x.startswith("worktree/") for x in entries):
            return {"status": "blocked", "reason": "worktree_backup_empty"}
        head = _git(folder, "rev-parse", "HEAD")
        if head.returncode:
            return {"status": "blocked", "reason": "git_head_unavailable"}
        manifest = {
            "schema": 1, "event_id": event_id, "base_or_current_head": head.stdout.strip(),
            "worktree_original": str(folder), "owner_original": str(owner),
            "result_status": _existing_report(c, event_id).get("status", "missing"),
            "original_logs_preserved": True,
            "files": {name: {"sha256": sha, "size": size}
                      for name, (_, sha, size) in entries.items()},
        }
        if not final.exists() and not final.is_symlink():
            with zipfile.ZipFile(partial, "x", compression=zipfile.ZIP_DEFLATED,
                                 allowZip64=True) as z:
                for name, (path, _, _) in entries.items():
                    z.write(path, arcname=name)
                z.writestr("RECOVERY-MANIFEST.json", json.dumps(
                    manifest, ensure_ascii=False, sort_keys=True, indent=2))
            if not _archive_matches(partial, entries, event_id):
                return {"status": "blocked", "reason": "archive_checksum_validation_failed"}
            partial.rename(final)
        if not _archive_matches(final, entries, event_id):
            return {"status": "blocked", "reason": "existing_archive_conflict"}
        # Re-read originals and validate SHA after archival, before removal.
        if _inventory(roots) != entries:
            return {"status": "blocked", "reason": "files_changed_since_backup"}
        state, why = eligibility(c, event_id, legacy=legacy)
        if state != "recoverable":
            return {"status": "blocked", "reason": "worktree_state_changed:" + why}
        if _git(folder, "rev-parse", "HEAD").stdout.strip() != head.stdout.strip():
            return {"status": "blocked", "reason": "head_changed_since_backup"}
        changed = _git(folder, "status", "--porcelain=v1", "--untracked-files=all")
        tracked = _git(folder, "ls-files", "-z", "--cached")
        if changed.returncode or tracked.returncode:
            return {"status": "blocked", "reason": "git_status_or_index_unavailable"}
        # Git porcelain omits IGNORED bin/obj artifacts. If tracked source is
        # clean but ignored generated files remain, non-force worktree removal
        # fails and the slot leaks. Force is safe only after every file was
        # independently archived, checksummed and re-inventoried above.
        known = set(filter(None, tracked.stdout.split("\0")))
        worktree_files = {
            name[len("worktree/"):]
            for name in entries if name.startswith("worktree/")
        }
        extras = worktree_files - known - {".git"}
        force_required = bool(changed.stdout.strip() or extras)
        args = ("worktree", "remove", "--force", str(folder)) if force_required else (
            "worktree", "remove", str(folder))
        removed = _git(owner, *args)
        if removed.returncode:
            return {"status": "blocked", "reason": "git_remove_failed_backup_preserved",
                    "archive": str(final)}
        return {"status": "archived", "reason": "sha256_verified_backup_and_git_worktree_removed",
                "event_id": event_id, "archive": str(final),
                "file_count": len(entries)}
    except (OSError, ValueError, subprocess.TimeoutExpired, zipfile.BadZipFile) as exc:
        return {"status": "blocked", "reason": type(exc).__name__}
    finally:
        # Partial data may be incomplete. Keep it for manual examination; never
        # remove or overwrite an archive automatically after a failed attempt.
        pass
