"""Publish an independently tested repair candidate as a DRAFT GitHub PR.

No PR is created unless a same-version visual diagnosis + isolated source
and registered regression test changes already passed in pipeline.run_one.
The private recent.log and screen.png are NEVER staged or uploaded.
GitHub CLI must be installed and authenticated separately from Codex.
Never merges, releases, or interacts with the running game.
"""
from __future__ import annotations

import re
import subprocess
from pathlib import Path, PurePosixPath

REPO = "skwlrna/Mbi_auto_update"
GIT_REMOTES = (
    "https://github.com/skwlrna/Mbi_auto_update.git",
    "https://github.com/skwlrna/Mbi_auto_update",
    "git@github.com:skwlrna/Mbi_auto_update.git",
    "ssh://git@github.com/skwlrna/Mbi_auto_update.git",
)
KEY_RE = re.compile(r"[a-f0-9]{24}\Z")
SHA_RE = re.compile(r"[a-f0-9]{40}\Z")
URL_RE = re.compile(r"https://github\.com/skwlrna/Mbi_auto_update/pull/[1-9]\d*\Z")


class PublishBlocked(ValueError):
    """Do not automatically retry a publishing problem."""


def command(args: list[str], cwd: Path, timeout: int = 90) -> str:
    try:
        process = subprocess.run(args, cwd=cwd, text=True, capture_output=True,
                                 check=False, timeout=timeout)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise PublishBlocked("Local Git or GitHub CLI process unavailable or timed out") from exc
    if process.returncode:
        # stderr/stdout can include private repository URLs or credentials.
        raise PublishBlocked("Command rejected: " + args[0] + " " + args[1])
    return process.stdout.strip()


def validated_candidate(state, key: str, repo: Path, analysis: dict) -> tuple[Path, list[str], str]:
    if not KEY_RE.fullmatch(key):
        raise PublishBlocked("Invalid incident fingerprint")
    if analysis.get("status") != "candidate_passed" or not analysis.get("candidate_verified"):
        raise PublishBlocked("Candidate not verified")
    if analysis.get("error_version") != analysis.get("source_version"):
        raise PublishBlocked("Incident version differs from source version")
    candidate = analysis.get("candidate")
    if not isinstance(candidate, dict) or candidate.get("status") != "candidate_passed":
        raise PublishBlocked("Candidate did not pass regression")
    if candidate.get("test_returncode") != 0 or not candidate.get("real_game_verified") is False:
        raise PublishBlocked("Candidate verification metadata missing")
    source_sha = candidate.get("source_sha")
    if not isinstance(source_sha, str) or not SHA_RE.fullmatch(source_sha):
        raise PublishBlocked("Invalid recorded source SHA")
    workspace = state.folder / "workspaces" / key / "repo"
    if not workspace.is_dir() or workspace.is_symlink():
        raise PublishBlocked("Candidate worktree missing")
    if Path(candidate.get("workspace", "")).resolve() != workspace.resolve():
        raise PublishBlocked("Candidate worktree path mismatch")
    files = candidate.get("changed_files")
    if not isinstance(files, list) or not 2 <= len(files) <= 12 or len(files) != len(set(files)):
        raise PublishBlocked("Candidate file count unsafe")
    import repair
    if sorted(repair.candidate_files(workspace)) != sorted(files):
        raise PublishBlocked("Candidate files differ from tested set")
    source_ok = False
    test_folder = Path(candidate.get("test", "missing")).parent.as_posix() + "/"
    test_ok = False
    for filename in files:
        if not isinstance(filename, str) or not filename.endswith(".cs"):
            raise PublishBlocked("Only C# source and regression files may be published")
        path = PurePosixPath(filename)
        if path.is_absolute() or ".." in path.parts:
            raise PublishBlocked("Invalid candidate path")
        if filename == "source/FishingAutomation/UpdateManager.cs":
            raise PublishBlocked("Auto-repair cannot change released version")
        if filename.startswith("source/FishingAutomation/"):
            source_ok = True
        elif filename.startswith(test_folder) and filename.startswith("tests/"):
            test_ok = True
        else:
            raise PublishBlocked("Only registered regression test may be published")
        local_file = workspace.joinpath(*path.parts)
        if not local_file.is_file() or local_file.is_symlink() or local_file.stat().st_size > 256_000:
            raise PublishBlocked("Candidate file missing, symlinked or excessive")
    if not source_ok or not test_ok:
        raise PublishBlocked("Missing matching source and regression changes")
    if command(["git", "rev-parse", "HEAD"], workspace) != source_sha:
        raise PublishBlocked("Candidate no longer based on tested source")
    if command(["git", "rev-parse", "HEAD"], repo) != source_sha:
        raise PublishBlocked("Original checkout changed after diagnosis")
    origin = command(["git", "remote", "get-url", "origin"], repo)
    if origin not in GIT_REMOTES:
        raise PublishBlocked("Unexpected origin remote; GitHub publishing vetoed")
    if command(["git", "ls-remote", "origin", "refs/heads/main"], repo).split("\t")[0] != source_sha:
        raise PublishBlocked("GitHub main has moved; review manually before PR")
    # No AI payload or private runtime evidence is included in PR.
    return workspace, sorted(files), source_sha


def publish(state, key: str, repo: Path, analysis: dict) -> str:
    """Commit tested code+test files, push new branch and open a DRAFT PR once.

    Errors are intentionally non-retryable by the watcher: e.g. a push could
    succeed while gh pr create fails, so a retry could create ambiguity.
    """
    workspace, files, source_sha = validated_candidate(state, key, repo, analysis)
    branch = "auto-repair/" + key + "-" + source_sha[:8]
    exists = command(["git", "ls-remote", "--heads", "origin",
                      "refs/heads/" + branch], repo)
    if exists:
        raise PublishBlocked("Repair branch already exists; no repeated push")
    if command(["git", "diff", "--check"], workspace):
        raise PublishBlocked("Invalid whitespace in candidate diff")
    # Git's -- terminates options for arbitrary model-selected filenames.
    command(["git", "add", "--", *files], workspace)
    staged = command(["git", "diff", "--cached", "--name-only", "-z"], workspace)
    if sorted(x for x in staged.split("\x00") if x) != files:
        raise PublishBlocked("Staged files changed unexpectedly")
    command(["git", "diff", "--cached", "--check"], workspace)
    # Local-only branch name; no force push or release tag.
    command(["git", "switch", "-c", branch], workspace)
    command(["git", "commit", "-m", "fix(repair): verified candidate " + key], workspace)
    # No force, no upstream changes, no pushing main.
    command(["git", "push", "origin", "HEAD:refs/heads/" + branch], workspace, 180)
    title = "fix(repair): verified candidate " + key[:12]
    body = (
        "Automated **draft** candidate. Do not merge without reviewing the source, "
        "Windows CI, and a same-version reproduction in the game.\n\n"
        "- Incident fingerprint: \x60" + key + "\x60\n"
        "- Source SHA before repair: \x60" + source_sha + "\x60\n"
        "- Incident/source version: \x60" + analysis["source_version"] + "\x60\n"
        "- Verified locally: registered .NET regression returned 0\n"
        "- Actual game verification: **not performed**\n"
        "- Only C# source and regression tests included; no image/recent.log or tokens\n"
        "- **Never auto-merge or release**\n"
    )
    response = command(["gh", "pr", "create", "--repo", REPO,
                        "--base", "main", "--head", branch, "--draft",
                        "--title", title, "--body", body], workspace, 180)
    urls = [line.strip() for line in response.splitlines() if URL_RE.fullmatch(line.strip())]
    if len(urls) != 1:
        raise PublishBlocked("GitHub PR URL missing from gh response; inspect manually")
    return urls[0]
