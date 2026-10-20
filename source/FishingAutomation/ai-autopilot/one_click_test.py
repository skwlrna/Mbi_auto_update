"""One-click PC offline smoke test. No game input, Codex, PR or updater.

Uses only a trusted installed BUILD_COMMIT and a clean matching local source.
A git archive is extracted to a disposable temp directory, so .NET builds
cannot mutate the installed macro, its saved F9 ledger or the source checkout.
"""
from __future__ import annotations

import datetime as dt
import json
import os
import re
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time

from release_source import (release_commit, check_checkout, prepare_source,
                            RELEASE_MARKER, SourceBlocked)

STEPS = (
    ("ai_autofix", [sys.executable, "-B", "-m", "unittest", "discover",
                    "-s", "tests/ai-autopilot", "-p", "test_*.py"], 240),
    ("f9_altering", ["dotnet", "run", "--project",
                     "tests/altering/Regression.csproj", "-c", "Release"], 300),
    ("n02_resume", ["dotnet", "run", "--project",
                    "tests/n02-resume/Regression.csproj", "-c", "Release",
                    "-r", "win-x64"], 300),
    ("f05_ledger", ["dotnet", "run", "--project",
                    "tests/f05-consumption/Regression.csproj", "-c", "Release",
                    "-r", "win-x64"], 300),
    ("combined_scheduler", ["dotnet", "run", "--project",
                            "tests/combined-scheduler/Regression.csproj",
                            "-c", "Release", "-r", "win-x64"], 300),
)
MAX_ARCHIVE_FILES = 18000
MAX_ARCHIVE_BYTES = 220 * 1024 * 1024
RESULT_ROOT = Path(os.environ.get("LOCALAPPDATA") or
                   Path.home() / "AppData" / "Local") / "MabiAuto" / "diagnostics" / "one-click"


def release_snapshot(home: Path, expected: str) -> tuple[Path | None, str]:
    """No clone/network: use only already present, pinned clean checkouts."""
    candidates = [home / "MabiAutoAI" / "source-snapshots" / expected,
                  home / "MabiAutoAI" / "repo"]
    for candidate in candidates:
        valid, _ = check_checkout(candidate, expected)
        if valid:
            return candidate, "verified_local_pinned_source"
    return None, "pinned_release_source_missing_or_unverified"


def safe_extract_archive(archive: Path, destination: Path) -> int:
    """Reject tar traversal, link entries, unusual members and oversized data."""
    count, total = 0, 0
    with tarfile.open(archive, "r:") as tar:
        for member in tar:
            count += 1
            total += member.size
            if count > MAX_ARCHIVE_FILES or total > MAX_ARCHIVE_BYTES:
                raise ValueError("source_archive_limit_exceeded")
            name = PurePosixPath(member.name)
            if (name.is_absolute() or ".." in name.parts or
                    not name.parts or chr(92) in member.name or
                    not (member.isfile() or member.isdir())):
                raise ValueError("source_archive_unsafe_member")
            target = destination.joinpath(*name.parts)
            if not target.resolve().is_relative_to(destination.resolve()):
                raise ValueError("source_archive_path_escape")
            if member.isdir():
                target.mkdir(parents=True, exist_ok=True)
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            src = tar.extractfile(member)
            if src is None:
                raise ValueError("source_archive_entry_unreadable")
            with src, target.open("xb") as out:
                shutil.copyfileobj(src, out)
    return count


def check_step(argv: list[str], cwd: Path, limit: int) -> tuple[str, str]:
    # Arbitrary log content is not copied to reports or uploaded.
    try:
        result = subprocess.run(argv, cwd=str(cwd), capture_output=True,
                                timeout=limit, shell=False, check=False)
        return ("passed", "exit_0") if result.returncode == 0 else (
            "failed", "test_nonzero_exit")
    except subprocess.TimeoutExpired:
        return "failed", "test_timeout"
    except OSError:
        return "failed", "test_launch_unavailable"


def has_dotnet_8_sdk() -> bool:
    """Only an actual .NET 8 SDK can compile these C# regression projects.

    'dotnet.exe' alone often means the runtime is present, not the SDK.
    """
    try:
        checked = subprocess.run(
            ["dotnet", "--list-sdks"], capture_output=True, text=True,
            encoding="utf-8", errors="replace", timeout=15, shell=False)
        return checked.returncode == 0 and any(
            re.match(r"^8\\.0\\.\\d+\\s+\\[", line.strip())
            for line in checked.stdout.splitlines())
    except (OSError, subprocess.TimeoutExpired):
        return False


def run_once(home: Path, output_root: Path, *,
             locate=shutil.which, marker=None) -> dict:
    report = {
        "schema": 1, "kind": "one_click_pc_offline_smoke",
        "startedUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "result": "blocked_environment",
        "gameTested": False, "gameInputSent": False, "codexExecuted": False,
        "prCreated": False, "autoUpdatePerformed": False,
        "tests": [], "source": "not_verified"
    }
    try:
        # Distinguish tools from mismatched source so the user never has to
        # guess from a generic "Git/.NET/SHA" failure dialog.
        if not locate("git"):
            report["reason"] = "git_missing"
            return report
        if not locate("dotnet") or not has_dotnet_8_sdk():
            report["reason"] = "dotnet_sdk_8_missing"
            return report
        try:
            expected = release_commit(marker) if marker is not None else release_commit()
        except (SourceBlocked, OSError, ValueError):
            report["reason"] = "installed_build_commit_unverified"
            return report
        source, proof = release_snapshot(home, expected)
        if source is None:
            # No PowerShell/manual clone needed. Existing trusted release-source
            # preparation may download a clean pinned main snapshot, but can
            # never update the installed macro or select a mismatched branch.
            try:
                selected = prepare_source({
                    "repo": str(home / "MabiAutoAI" / "repo"),
                    "source_snapshots_dir": str(home / "MabiAutoAI" / "source-snapshots")
                }, marker=marker if marker is not None else RELEASE_MARKER)
                source = Path(selected["repo"])
                valid, _ = check_checkout(source, expected)
                if not valid:
                    report["reason"] = "prepared_source_revision_mismatch"
                    return report
                proof = "newly_prepared_pinned_snapshot"
            except (SourceBlocked, OSError, subprocess.TimeoutExpired, ValueError):
                report["source"] = "pinned_source_unavailable"
                report["reason"] = "trusted_release_source_download_or_verification_failed"
                return report
        report["source"] = proof
        # Do not let a concurrent source checkout edit race the archive.
        with tempfile.TemporaryDirectory(prefix="mabiauto-oneclick-") as temp:
            root = Path(temp)
            archive = root / "source.tar"
            verified, _ = check_checkout(source, expected)
            if not verified:
                report["reason"] = "source_changed_before_archive"
                return report
            try:
                zipped = subprocess.run(
                    ["git", "-c", "core.hooksPath=NUL", "archive", "--format=tar",
                     "--output", str(archive), expected],
                    cwd=str(source), capture_output=True, timeout=90,
                    shell=False, check=False)
            except (OSError, subprocess.TimeoutExpired):
                report["reason"] = "source_archive_unavailable"
                return report
            if zipped.returncode != 0:
                report["reason"] = "source_archive_failed"
                return report
            verified, _ = check_checkout(source, expected)
            if not verified:
                report["reason"] = "source_changed_during_archive"
                return report
            sandbox = root / "isolated-source"
            sandbox.mkdir()
            try:
                safe_extract_archive(archive, sandbox)
            except (OSError, ValueError, tarfile.TarError):
                report["reason"] = "source_archive_unsafe_or_unavailable"
                return report
            if not (sandbox / "source/FishingAutomation/FishingAutomation.csproj").is_file():
                report["reason"] = "isolated_source_incomplete"
                return report
            report["source"] = "isolated_pinned_release_archive"
            for label, argv, timeout in STEPS:
                step_started = time.monotonic()
                print("CHECK_START|" + label, flush=True)
                status, reason = check_step(argv, sandbox, timeout)
                report["tests"].append({
                    "id": label, "status": status, "reason": reason,
                    "seconds": int(time.monotonic() - step_started)
                })
                print("CHECK_RESULT|" + label + "|" + status, flush=True)
                if status != "passed":
                    report["reason"] = label + "_" + reason
                    report["result"] = "tests_failed"
                    return report
            report["reason"] = "offline_smoke_passed_not_game_verified"
            report["result"] = "passed_offline"
            return report
    except (OSError, ValueError, TypeError):
        report["reason"] = "one_click_unexpected_environment_error"
        return report
    finally:
        report["endedUtc"] = dt.datetime.now(dt.timezone.utc).isoformat()
        output_root.mkdir(parents=True, exist_ok=True)
        destination = output_root / "latest-result.json"
        staging = output_root / "latest-result.json.tmp"
        staging.write_text(json.dumps(report, ensure_ascii=False, indent=2),
                           encoding="utf-8")
        staging.replace(destination)


def main() -> int:
    report = run_once(Path.home(), RESULT_ROOT)
    print("ONE_CLICK_RESULT|" + report["result"], flush=True)
    return 0 if report["result"] == "passed_offline" else 2


if __name__ == "__main__":
    sys.exit(main())
