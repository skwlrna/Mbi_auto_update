"""Local, bounded, read-only prerequisites for the real Codex repair pipeline.

Never invoke Codex, the game, GitHub, tests, or any updater. A passing report
does NOT mean a patch was created or tested. Paths and CLI output stay local.
"""
from pathlib import Path
import shutil
import subprocess
from typing import Any

STAGES = ("evidence", "source", "codex", "regressions", "draft_pr")


def readiness(config: dict[str, Any], opted_in: bool = False,
              locate=shutil.which, execute=subprocess.run) -> dict[str, Any]:
    repo = Path(config["repo"]).expanduser().resolve()
    scripts = Path(__file__).resolve().parent
    source_ok = repo.is_dir() and (repo / "source/FishingAutomation/FishingAutomation.csproj").is_file()
    clean = False
    if source_ok and locate("git"):
        try:
            top = execute(["git", "rev-parse", "--show-toplevel"], cwd=str(repo),
                          capture_output=True, text=True, timeout=10, shell=False)
            dirty = execute(["git", "status", "--porcelain", "--untracked-files=no"],
                            cwd=str(repo), capture_output=True, text=True, timeout=10, shell=False)
            clean = (top.returncode == 0 and
                     Path(top.stdout.strip()).resolve() == repo and
                     dirty.returncode == 0 and not dirty.stdout.strip())
        except (OSError, subprocess.TimeoutExpired, ValueError):
            clean = False
    regression_paths = (
        "tests/altering/Regression.csproj",
        "tests/altering-screen-integration/Regression.csproj",
        "tests/crafting/Regression.csproj",
        "tests/production-ui/Regression.csproj",
    )
    steps = {
        "evidence": (scripts / "autopilot.py").is_file(),
        "source": source_ok and clean,
        "codex": bool(locate("python") and locate("git") and locate("codex")),
        "regressions": bool(locate("dotnet") and source_ok and
                            all((repo / path).is_file() for path in regression_paths)),
        "draft_pr": bool(locate("gh") and opted_in),
    }
    return {
        "schema": 1,
        "kind": "local_readiness_only",
        "result": "ready_for_manual_trial" if all(steps[x] for x in STAGES[:-1])
                  else "needs_setup",
        "stages": steps,
        "github_pr_optional": True,
        "codex_login_verified": False,
        "codex_executed": False,
        "patch_created": False,
        "tests_executed": False,
        "draft_pr_created": False,
        "game_tested": False,
        "automatic_merge_deploy": False,
        "next_step": "Test one actual error on the user's Windows PC with Codex logged in."
    }
