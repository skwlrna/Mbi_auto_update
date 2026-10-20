"""Opt-in, fail-closed GitHub draft-PR publisher (never auto-merge/deploy)."""
from __future__ import annotations
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
from typing import Any

REPOSITORY = "skwlrna/Mbi_auto_update"
PR_URL = re.compile(r"^https://github\.com/skwlrna/Mbi_auto_update/pull/[1-9]\d*/?$")
ORIGINS = frozenset({
    "https://github.com/skwlrna/Mbi_auto_update",
    "https://github.com/skwlrna/Mbi_auto_update.git",
    "git@github.com:skwlrna/Mbi_auto_update",
    "git@github.com:skwlrna/Mbi_auto_update.git",
    "ssh://git@github.com/skwlrna/Mbi_auto_update.git",
})


def user_opted_in() -> bool:
    """Read preference at publication time, never cache a startup setting."""
    local = os.environ.get("LOCALAPPDATA", "")
    if not local:
        return False
    path = Path(local) / "MabiAuto" / "ai-autopilot.json"
    try:
        if path.is_symlink() or path.stat().st_size > 4096:
            return False
        return json.loads(path.read_text(encoding="utf-8-sig")).get("AutoCreatePr") is True
    except (OSError, ValueError, TypeError, AttributeError):
        return False


def trusted_origin(url: str) -> bool:
    return url.strip() in ORIGINS


def safe_branch(branch: str) -> bool:
    return bool(re.fullmatch(r"ai/autopilot-[0-9a-f]{16}", branch))


def safe_candidate_path(name: str) -> bool:
    """Independent host-side allowlist, checked again before GitHub upload."""
    if not isinstance(name, str) or not name or chr(0) in name:
        return False
    if chr(92) in name or name.startswith("./"):
        return False
    path = PurePosixPath(name)
    if (path.is_absolute() or len(path.parts) < 2 or ".." in path.parts
            or "." in path.parts or str(path) != name):
        return False
    if any(part.lower() in {"bin", "obj", ".git", ".github", "runtime"}
           for part in path.parts):
        return False
    if path.suffix.lower() in {".ps1", ".cmd", ".bat", ".sh", ".exe",
                               ".dll", ".pfx", ".key"}:
        return False
    if (name.startswith("source/FishingAutomation/ai-autopilot/") or
            name.startswith("source/tools/") or
            name in {"source/FishingAutomation/UpdateManager.cs",
                     "source/FishingAutomation/FishingAutomation.csproj"}):
        return False
    return name.startswith(("source/", "tests/", "changes/tests/"))


def run(argv: list[str], cwd: Path, timeout: int = 60) -> subprocess.CompletedProcess[str]:
    return subprocess.run(argv, cwd=cwd, capture_output=True, text=True,
                          encoding="utf-8", errors="replace", shell=False,
                          timeout=timeout)


def publish_verified_draft_pr(
    worktree: Path, branch: str, event_id: str, changed: list[str],
    result_dir: Path, verified_tests: list[dict[str, Any]]
) -> dict[str, str]:
    if not user_opted_in():
        return {"status": "disabled", "reason": "user_opt_in_required"}
    if not safe_branch(branch) or not re.fullmatch(r"[0-9a-f]{16}", event_id):
        return {"status": "blocked", "reason": "untrusted_branch_or_event"}
    if (not changed or len(changed) > 8 or len(set(changed)) != len(changed)
            or any(not safe_candidate_path(name) for name in changed)):
        return {"status": "blocked", "reason": "changed_file_allowlist_failed"}
    root = worktree.resolve()
    for name in changed:
        target = worktree / name
        if target.is_symlink() or not target.is_file() or (
                not target.resolve().is_relative_to(root)):
            return {"status": "blocked", "reason": "candidate_path_not_regular_file"}
    if not verified_tests or not all(item.get("ok") is True for item in verified_tests):
        return {"status": "blocked", "reason": "tests_not_verified"}
    if not shutil.which("gh"):
        return {"status": "blocked", "reason": "github_cli_not_installed"}
    try:
        origin = run(["git", "remote", "get-url", "origin"], worktree)
        if origin.returncode or not trusted_origin(origin.stdout):
            return {"status": "blocked", "reason": "origin_repository_mismatch"}
        identity = run(["git", "branch", "--show-current"], worktree)
        if identity.returncode or identity.stdout.strip() != branch:
            return {"status": "blocked", "reason": "branch_identity_mismatch"}
        logged_in = run(["gh", "auth", "status", "--hostname", "github.com"], worktree, 30)
        if logged_in.returncode:
            return {"status": "blocked", "reason": "github_cli_login_required"}
        # Do not execute potentially untrusted user/AI-provided hooks.
        hooks = result_dir / "disabled-git-hooks"
        hooks.mkdir(exist_ok=True)
        safe = ["git", "-c", "core.hooksPath=" + str(hooks)]
        staged = run(safe + ["add", "--", *changed], worktree, 40)
        if staged.returncode:
            return {"status": "blocked", "reason": "git_stage_failed"}
        diff = run(["git", "diff", "--cached", "--name-only", "-z"], worktree, 30)
        if diff.returncode or set(diff.stdout.split("\0")) - {""} != set(changed):
            return {"status": "blocked", "reason": "staged_file_set_mismatch"}
        patch = run(["git", "diff", "--cached", "--no-ext-diff", "--binary"], worktree, 40)
        if patch.returncode or len(patch.stdout) > 512000 or (
            re.search(r"(?i)(?:gh[pousr]_[A-Za-z0-9_]{15,}|sk-[A-Za-z0-9_-]{16,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)", patch.stdout)
        ):
            return {"status": "blocked", "reason": "candidate_secret_or_patch_size_guard"}
        committed = run(safe + ["-c", "user.name=MabiAuto AI Review",
                                 "-c", "user.email=mabiauto-ai@users.noreply.github.com",
                                 "commit", "-m", "AI candidate: " + event_id], worktree, 70)
        if committed.returncode:
            return {"status": "blocked", "reason": "candidate_commit_failed"}
        if not user_opted_in():
            return {"status": "disabled", "reason": "user_disabled_before_publication"}
        # Candidate must be based on the currently published main tip.
        # Never push a PR from an older (or newer) release revision.
        local_base = run(["git", "rev-parse", "HEAD^"], worktree, 15)
        remote_base = run(["git", "ls-remote", "origin", "refs/heads/main"], worktree, 35)
        fields = remote_base.stdout.strip().split()
        if (local_base.returncode or remote_base.returncode or
            len(fields) != 2 or fields[0] != local_base.stdout.strip()):
            return {"status": "blocked", "reason": "pr_base_mismatch_with_current_main"}
        pushed = run(safe + ["push", "origin", "HEAD:refs/heads/" + branch], worktree, 100)
        if pushed.returncode:
            return {"status": "blocked", "reason": "review_branch_push_failed"}
        body = result_dir / "draft-pr-body.md"
        body.write_text(
            "## MabiAuto AI candidate — NOT DEPLOYED\n\n"
            "MCP event: " + event_id + "\n\n"
            "### Modified paths\n" + "".join("- " + name + "\n" for name in changed) +
            "\n### Offline verification\n" +
            "".join("- Test " + str(t.get("test", "")) + ": PASS\n" for t in verified_tests) +
            "\nLocal host tests verify only this initial candidate; final PR "
            "Windows CI pending and NOT checked by the PC.\n\n"
            "DRAFT ONLY. Human review and real game testing required; "
            "no automatic merge, release, or live game change.\n",
            encoding="utf-8")
        created = run(["gh", "pr", "create", "--repo", REPOSITORY,
                       "--base", "main", "--head", branch,
                       "--draft", "--title", "AI review: " + event_id,
                       "--body-file", str(body)], worktree, 70)
        url = created.stdout.strip().splitlines()[-1] if created.stdout.strip() else ""
        if created.returncode or not PR_URL.fullmatch(url):
            return {"status": "blocked", "reason": "pr_creation_failed_after_branch_push"}
        candidate = run(["git", "rev-parse", "HEAD"], worktree, 15)
        return {"status": "draft_created", "url": url,
                "head_sha": candidate.stdout.strip() if candidate.returncode == 0 else "",
                "ci_status": "not_checked", "game_validated": "false",
                "reason": "manual_ci_review_and_live_game_required"}
    except (OSError, subprocess.TimeoutExpired):
        return {"status": "blocked", "reason": "github_publish_io_or_timeout"}
