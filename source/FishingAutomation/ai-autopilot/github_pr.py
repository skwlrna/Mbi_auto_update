"""Opt-in, fail-closed GitHub draft-PR publisher (never auto-merge/deploy)."""
from __future__ import annotations
import json
import os
from pathlib import Path
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
    return bool(re.fullmatch(r"ai/autopilot-error_[A-Za-z0-9_-]{8,100}", branch))


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
    if not safe_branch(branch) or not re.fullmatch(r"error_[A-Za-z0-9_-]{8,100}", event_id):
        return {"status": "blocked", "reason": "untrusted_branch_or_event"}
    if not changed or len(changed) > 8 or any(not (
        name.startswith("source/") or name.startswith("tests/") or
        name.startswith("changes/tests/")) for name in changed):
        return {"status": "blocked", "reason": "changed_file_allowlist_failed"}
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
        committed = run(safe + ["-c", "user.name=MabiAuto AI Review",
                                 "-c", "user.email=mabiauto-ai@users.noreply.github.com",
                                 "commit", "-m", "AI candidate: " + event_id], worktree, 70)
        if committed.returncode:
            return {"status": "blocked", "reason": "candidate_commit_failed"}
        if not user_opted_in():
            return {"status": "disabled", "reason": "user_disabled_before_publication"}
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
            "\nDRAFT ONLY. Human review and real game testing required; "
            "no automatic merge, release, or live game change.\n",
            encoding="utf-8")
        created = run(["gh", "pr", "create", "--repo", REPOSITORY,
                       "--base", "main", "--head", branch,
                       "--draft", "--title", "AI review: " + event_id,
                       "--body-file", str(body)], worktree, 70)
        url = created.stdout.strip().splitlines()[-1] if created.stdout.strip() else ""
        if created.returncode or not PR_URL.fullmatch(url):
            return {"status": "blocked", "reason": "pr_creation_failed_after_branch_push"}
        return {"status": "draft_created", "url": url,
                "reason": "manual_review_and_merge_required"}
    except (OSError, subprocess.TimeoutExpired):
        return {"status": "blocked", "reason": "github_publish_io_or_timeout"}
