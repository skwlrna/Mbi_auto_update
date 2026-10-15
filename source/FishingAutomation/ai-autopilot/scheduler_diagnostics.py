#!/usr/bin/env python3
"""Opt-in, local-only scheduler diagnostics for MabiAuto.

No downloads, installation, service, GitHub upload, game input, automatic
retry or background daemon. Python 3.10+ standard library only.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
from datetime import datetime, timezone

TOOLS = {"debugview": "dbgviewcli.exe", "procdump": "procdump.exe", "imagemagick": "magick.exe"}
ENV_TOOLS = {"debugview": "MABIAUTO_DBGVIEWCLI", "procdump": "MABIAUTO_PROCDUMP", "imagemagick": "MABIAUTO_MAGICK"}
IMAGE_TYPES = frozenset({".png", ".jpg", ".jpeg", ".bmp"})


def default_outdir() -> Path:
    base = os.environ.get("LOCALAPPDATA") or str(Path.home() / "AppData" / "Local")
    return Path(base) / "MabiAuto" / "scheduler-diagnostics"


def locate(tool: str) -> Path | None:
    requested = os.environ.get(ENV_TOOLS[tool])
    if requested:
        p = Path(requested).expanduser()
        return p.resolve() if p.is_file() else None
    found = shutil.which(TOOLS[tool]) or shutil.which(TOOLS[tool].removesuffix(".exe"))
    return Path(found).resolve() if found and Path(found).is_file() else None


def pid_value(raw: str) -> int:
    try:
        n = int(raw)
    except ValueError as e:
        raise argparse.ArgumentTypeError("PID must be a positive integer") from e
    if n < 1 or n > 0x7fffffff:
        raise argparse.ArgumentTypeError("PID must be a positive Windows PID")
    return n


def image_file(raw: str) -> Path:
    path = Path(raw).expanduser()
    if path.is_symlink() or not path.is_file() or path.suffix.lower() not in IMAGE_TYPES:
        raise ValueError("Expected a local non-symlink PNG/JPEG/BMP file")
    if path.stat().st_size > 16 * 1024 * 1024:
        raise ValueError("Image exceeds the 16 MiB local diagnostics limit")
    return path.resolve()


def output_directory(raw: str | None) -> Path:
    path = Path(raw).expanduser() if raw else default_outdir()
    if path.is_symlink():
        raise ValueError("Refusing symlink output directory")
    path.mkdir(parents=True, exist_ok=True)
    return path.resolve()


def event_directory(base: Path, action: str) -> Path:
    when = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S_%fZ")
    p = base / (when + "_" + action)
    p.mkdir(mode=0o700, exist_ok=False)
    return p


def run_bounded(args: list[str], seconds: int, stdout: Path, stderr: Path) -> int:
    creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0) if os.name == "nt" else 0
    with stdout.open("wb") as out, stderr.open("wb") as err:
        try:
            result = subprocess.run(args, stdin=subprocess.DEVNULL, stdout=out,
                                    stderr=err, shell=False, timeout=seconds,
                                    creationflags=creationflags, check=False)
        except subprocess.TimeoutExpired as e:
            raise RuntimeError("Tool exceeded the bounded execution time") from e
    return result.returncode


def doctor() -> dict:
    return {"available": {name: bool(locate(name)) for name in TOOLS},
            "debug_output_opt_in": os.environ.get("MABIAUTO_SCHEDULER_DEBUG") == "1",
            "note": "No capture runs until an explicit command is invoked; never uploads data."}


def capture(pid: int, seconds: int, root: Path) -> dict:
    exe = locate("debugview")
    if exe is None:
        raise RuntimeError("dbgviewcli.exe is not installed/configured")
    try:
        status = subprocess.run([str(exe), "--status"], capture_output=True,
                                text=True, errors="replace", timeout=5, check=False)
    except subprocess.TimeoutExpired as e:
        raise RuntimeError("DebugView status check timed out") from e
    if re.search(r"(?im)^running\s*=\s*true\s*$", status.stdout):
        raise RuntimeError("Another DebugView capture is active; not taking ownership")
    if status.returncode not in (0, 1):
        raise RuntimeError("DebugView status check failed")
    dest = event_directory(root, "debugview")
    argv = [str(exe), "--no-banner", "--no-kernel", "--win32",
            "--pid-filter", str(pid), "--duration", str(seconds),
            "--max-lines", "2000", "--tail", "300", "--format", "csv"]
    code = run_bounded(argv, seconds + 15, dest / "debug.csv", dest / "tool.stderr.txt")
    result = {"action": "debugview", "exit_code": code, "pid": pid,
              "duration_seconds": seconds, "output": str(dest),
              "status": "captured" if code == 0 else "tool_failed"}
    (dest / "manifest.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    return result


def dump(pid: int, root: Path, allow_sensitive: bool) -> dict:
    if not allow_sensitive:
        raise RuntimeError("Explicit --allow-sensitive-dump is required; dumps may contain secrets")
    exe = locate("procdump")
    if exe is None:
        raise RuntimeError("procdump.exe is not installed/configured")
    dest = event_directory(root, "procdump")
    argv = [str(exe), "-accepteula", "-mm", "-n", "1", "-at", "30", str(pid), str(dest)]
    code = run_bounded(argv, 45, dest / "tool.stdout.txt", dest / "tool.stderr.txt")
    result = {"action": "procdump", "exit_code": code, "pid": pid,
              "output": str(dest), "sensitive": True,
              "status": "captured" if code == 0 and any(dest.glob("*.dmp")) else "tool_failed"}
    (dest / "manifest.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    return result


def compare(baseline: Path, observed: Path, root: Path) -> dict:
    exe = locate("imagemagick")
    if exe is None:
        raise RuntimeError("magick.exe is not installed/configured")
    dest = event_directory(root, "imagemagick")
    difference = dest / "difference.png"
    argv = [str(exe), "compare", "-metric", "RMSE", str(baseline),
            str(observed), str(difference)]
    code = run_bounded(argv, 30, dest / "tool.stdout.txt", dest / "metric.stderr.txt")
    result = {"action": "imagemagick", "exit_code": code,
              "status": "compared" if code in (0, 1) and difference.is_file() else "tool_failed",
              "output": str(dest), "different": code == 1 if code in (0, 1) else None}
    (dest / "manifest.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="MabiAuto opt-in local scheduler diagnostics")
    parser.add_argument("--output", help="Local directory for diagnostic artifacts")
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("doctor", help="Check whether utilities are installed (no capture)")
    cap = commands.add_parser("capture", help="Bounded, PID-filtered DebugView capture")
    cap.add_argument("--pid", type=pid_value, required=True)
    cap.add_argument("--seconds", type=int, default=60)
    dmp = commands.add_parser("dump", help="One-time ProcDump mini dump (may contain secrets)")
    dmp.add_argument("--pid", type=pid_value, required=True)
    dmp.add_argument("--allow-sensitive-dump", action="store_true")
    cmp = commands.add_parser("compare", help="Compare two locally selected screenshots")
    cmp.add_argument("--baseline", required=True)
    cmp.add_argument("--observed", required=True)
    args = parser.parse_args(argv)
    try:
        if args.command == "doctor":
            result = doctor()
        else:
            if args.command == "capture" and not 5 <= args.seconds <= 600:
                raise ValueError("Capture duration must be between 5 and 600 seconds")
            root = output_directory(args.output)
            if args.command == "capture":
                result = capture(args.pid, args.seconds, root)
            elif args.command == "dump":
                result = dump(args.pid, root, args.allow_sensitive_dump)
            else:
                result = compare(image_file(args.baseline), image_file(args.observed), root)
        print(json.dumps(result, ensure_ascii=False))
        return 0 if result.get("status") not in ("tool_failed",) else 2
    except (OSError, ValueError, RuntimeError) as exc:
        print(json.dumps({"status": "blocked", "reason": str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
