"""Codex process-tree supervision for MabiAuto (no restart or deployment)."""
from __future__ import annotations
import os
from pathlib import Path
import re
import signal
import subprocess
import threading
import time
from typing import Callable

Progress = Callable[[str, int, int, str], None]


def terminate_tree(proc: subprocess.Popen) -> None:
    """Terminate ONLY this Codex invocation and its descendants."""
    if proc.poll() is not None:
        return
    try:
        if os.name == "nt":
            subprocess.run(["taskkill", "/PID", str(proc.pid), "/T", "/F"],
                           capture_output=True, timeout=12, check=False)
        else:
            os.killpg(proc.pid, signal.SIGKILL)
    except (OSError, subprocess.TimeoutExpired, ProcessLookupError):
        pass
    if proc.poll() is None:
        try:
            proc.kill()
        except OSError:
            pass
    try:
        proc.wait(timeout=8)
    except subprocess.TimeoutExpired:
        pass


def supervised_run(argv: list[str], cwd: Path, prompt: str,
                   stdout_path: Path, stderr_path: Path,
                   idle_seconds: int = 300, hard_seconds: int = 1200,
                   heartbeat_seconds: int = 45,
                   on_progress: Progress | None = None
                   ) -> tuple[int, str, str, str | None]:
    """Capture Codex output with idle and wall-clock deadlines.

    An idle/hard timeout blocks the current job, without retry.
    Use a process-group/tree kill so no Codex grandchild remains.
    No stdout/stderr content is displayed in the MabiAuto UI.
    """
    idle_seconds = max(60, int(idle_seconds))
    hard_seconds = max(idle_seconds + 30, int(hard_seconds))
    heartbeat_seconds = max(15, int(heartbeat_seconds))
    env = {k: v for k, v in os.environ.items() if not re.search(
        r"(?:GITHUB|GH_TOKEN|AWS_|AZURE_|PASSWORD|SECRET|PRIVATE_KEY|PAT_TOKEN)", k, re.I)}
    flags = subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0
    proc = subprocess.Popen(
        argv, cwd=str(cwd), stdin=subprocess.PIPE,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        text=True, encoding="utf-8", errors="replace",
        shell=False, env=env, bufsize=1, creationflags=flags,
        start_new_session=os.name != "nt")
    started = time.monotonic()
    last_output = [started]
    lock = threading.Lock()
    oversized = [False]
    total_chars = [0]
    parts: dict[str, list[str]] = {"stdout": [], "stderr": []}

    def drain(stream, destination: Path, kind: str) -> None:
        with destination.open("w", encoding="utf-8") as output:
            for line in stream:
                # Raw diagnostic files are local-only. No bytes are forwarded
                # to a cloud endpoint from this runner.
                output.write(line)
                with lock:
                    last_output[0] = time.monotonic()
                    total_chars[0] += len(line)
                    if total_chars[0] <= 8 * 1024 * 1024:
                        parts[kind].append(line)
                    else:
                        oversized[0] = True

    workers = [threading.Thread(target=drain, args=(proc.stdout, stdout_path, "stdout"), daemon=True),
               threading.Thread(target=drain, args=(proc.stderr, stderr_path, "stderr"), daemon=True)]
    for worker in workers:
        worker.start()
    issue = None
    try:
        assert proc.stdin is not None
        proc.stdin.write(prompt)
        proc.stdin.close()
        next_heartbeat = started + heartbeat_seconds
        while proc.poll() is None:
            instant = time.monotonic()
            with lock:
                silent = int(instant - last_output[0])
            elapsed = int(instant - started)
            if silent >= idle_seconds or elapsed >= hard_seconds:
                issue = "codex_stalled" if silent >= idle_seconds else "codex_hard_timeout"
                terminate_tree(proc)
                if on_progress:
                    on_progress("blocked", elapsed, silent, issue)
                break
            if instant >= next_heartbeat:
                if on_progress:
                    on_progress("running", elapsed, silent, "codex_running")
                next_heartbeat = instant + heartbeat_seconds
            time.sleep(1)
        try:
            proc.wait(timeout=8)
        except subprocess.TimeoutExpired:
            terminate_tree(proc)
            issue = "codex_hard_timeout"
    finally:
        for worker in workers:
            worker.join(timeout=6)
        for stream in (proc.stdin, proc.stdout, proc.stderr):
            if stream:
                try:
                    stream.close()
                except OSError:
                    pass
    if issue:
        return -1, "", "", issue
    if oversized[0]:
        return proc.returncode, "", "", "codex_output_over_limit"
    return proc.returncode, "".join(parts["stdout"]), "".join(parts["stderr"]), None
