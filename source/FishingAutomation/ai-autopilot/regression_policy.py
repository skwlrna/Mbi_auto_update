"""Allowlisted executable regression suites for AI-generated repair tests.

A successful build without a modified, registered regression test is REVIEW,
not a verified fix. Existing test projects only; no arbitrary test commands.
"""
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys

REGISTERED_TESTS = frozenset({
    "tests/altering/Program.cs",
    "tests/crafting/Program.cs",
    "tests/production-ui/Program.cs",
    "tests/altering-screen-integration/Program.cs",
    "tests/altering-visual/Program.cs",
    "tests/n02-resume/Program.cs",
    "tests/f05-consumption/Program.cs",
    "tests/combined-scheduler/Program.cs",
    "tests/loot-retry/Program.cs",
    "tests/retry-flow/Program.cs",
    "tests/gathering-diagnostic/Program.cs",
    "tests/gathering/Program.cs",
    "tests/inventory-loot/Program.cs",
    "tests/n03-input/Program.cs",
    "tests/cli-readonly/Program.cs",
})



# Critical scheduler source changes require the real combined-scheduler
# regression suite. An unrelated but registered test cannot authorize a
# scheduler repair. This list is deliberately narrow; arbitrary code edits
# must still satisfy the existing allowlist and global regression checks.
SCHEDULER_TEST = "tests/combined-scheduler/Program.cs"
# A build plus an unrelated registered test is NOT enough to validate
# edits to actual production screens. Keep the mapping narrow and specific.
SCREEN_TESTS = {
    "source/FishingAutomation/dungeon/CraftingScreen.cs": "tests/production-ui/Program.cs",
    "source/FishingAutomation/CraftingAutomation.cs": "tests/crafting/Program.cs",
    "source/FishingAutomation/CraftingHubLayout.cs": "tests/crafting/Program.cs",
    "source/FishingAutomation/dungeon/AlteringScreen.cs": "tests/altering-screen-integration/Program.cs",
    "source/FishingAutomation/AlteringRecipeLayout.cs": "tests/altering/Program.cs",
    "source/FishingAutomation/AlteringFacilityLayout.cs": "tests/altering/Program.cs",
    "source/FishingAutomation/dungeon/ProductionUiRuntime.cs": "tests/production-ui/Program.cs",
    "source/FishingAutomation/dungeon/InventoryBulkGatheringScreen.cs": "tests/gathering/Program.cs",
    "source/FishingAutomation/LifeSkillListLayout.cs": "tests/gathering/Program.cs",
}

# A general passing test is insufficient when Codex edits persistence,
# F9/abyss scheduling, food supply safety or screen-input ownership.
# Require a deterministic positive AND veto test in the relevant registered
# suite. Source-to-suite relationships are host-authored, not log-derived.
CRITICAL_SOURCE_TESTS: dict[str, tuple[str, ...]] = {
    "source/FishingAutomation/MultiAlteringCoordinator.cs": (
        "tests/altering/Program.cs", "tests/n02-resume/Program.cs"),
    "source/FishingAutomation/MultiAlteringBatchStore.cs": (
        "tests/altering/Program.cs", "tests/n02-resume/Program.cs"),
    "source/FishingAutomation/MainForm.MultiAltering.cs": (
        "tests/altering/Program.cs", "tests/n02-resume/Program.cs"),
    "source/FishingAutomation/MultiAlteringReadQuarantine.cs": (
        "tests/altering/Program.cs", "tests/n02-resume/Program.cs"),
    "source/FishingAutomation/RecursiveAlteringSupplyResolver.cs": (
        "tests/altering/Program.cs",),
    "source/FishingAutomation/MultiAlteringDependencyScheduler.cs": (
        "tests/altering/Program.cs", "tests/combined-scheduler/Program.cs"),
    "source/FishingAutomation/MultiAlteringConsumptionLedger.cs": (
        "tests/f05-consumption/Program.cs", "tests/altering/Program.cs"),
    "source/FishingAutomation/FacilityLaneState.cs": (
        "tests/altering/Program.cs", "tests/n02-resume/Program.cs"),
    "source/FishingAutomation/MainForm.CombinedAltering.cs": (
        "tests/combined-scheduler/Program.cs",),
}

SCHEDULER_SOURCES = frozenset({
    "source/FishingAutomation/SchedulerRunMonitor.cs",
    "source/FishingAutomation/SchedulerReadOnlyRetry.cs",
    "source/FishingAutomation/AbyssAlteringSchedulePolicy.cs",
    "source/FishingAutomation/MultiAlteringDependencyScheduler.cs",
    "source/FishingAutomation/MainForm.CombinedAltering.cs",
    "source/FishingAutomation/dungeon/ScenarioEngine.AbyssSchedulerExit.cs",
})


def changes_scheduler_source(path: str) -> bool:
    return (path in SCHEDULER_SOURCES or
            path.startswith("source/FishingAutomation/MainForm.Scheduler"))


def _present_in_base(worktree: Path, name: str) -> bool:
    """Host checks the installed source baseline, not paths claimed by Codex.

    Newly invented test projects are not registered by the agent. A real
    existing test suite was often missing from the hand-maintained allowlist;
    those are safe to discover and rerun in a fixed command shape.
    """
    if not (name.startswith("tests/") or name.startswith("changes/tests/")) or ":" in name or "\\" in name:
        return False
    try:
        check = subprocess.run(
            ["git", "-C", str(worktree), "cat-file", "-e", "HEAD:" + name],
            capture_output=True, timeout=15, shell=False)
        return check.returncode == 0
    except (OSError, subprocess.TimeoutExpired):
        return False


def _registered_csharp_test(name: str, worktree: Path) -> bool:
    if name in REGISTERED_TESTS:
        return True
    if not re.fullmatch(r"(?:tests|changes/tests)/[A-Za-z0-9_-]+/Program\.cs", name):
        return False
    project = str(PurePosixPath(name).parent / "Regression.csproj")
    return (_present_in_base(worktree, name)
            and _present_in_base(worktree, project)
            and (worktree / project).is_file()
            and not (worktree / project).is_symlink())


def _registered_python_test(name: str, worktree: Path) -> bool:
    if not re.fullmatch(r"tests/ai-autopilot/test_[A-Za-z0-9_]+\.py", name):
        return False
    target = worktree / name
    return (target.is_file() and not target.is_symlink()
            and _present_in_base(worktree, name))


def commands_for(changed: list[str], worktree: Path
                 ) -> tuple[list[list[str]], str | None]:
    modified_tests = [name for name in changed if name.startswith("tests/")
                      or name.startswith("changes/tests/")]
    if not modified_tests:
        return [], "regression_test_missing"
    csharp = [name for name in modified_tests if name.endswith("/Program.cs")]
    python = [name for name in modified_tests if name.endswith(".py")]
    if (len(csharp) + len(python) != len(modified_tests)
            or any(not _registered_csharp_test(name, worktree) for name in csharp)
            or any(not _registered_python_test(name, worktree) for name in python)):
        return [], "regression_test_suite_not_registered"

    projects = set()
    for name in csharp:
        project = str(PurePosixPath(name).parent / "Regression.csproj")
        if not (worktree / project).is_file() or (worktree / project).is_symlink():
            return [], "regression_test_project_missing"
        projects.add(project)
    production = [name for name in changed if name.startswith("source/")]
    if not production:
        return [], "production_source_patch_missing"

    # Python autopilot edits need an actual Python regression; game code
    # changes need an actual C# suite. Neither may substitute for the other.
    ai_changes = any(name.startswith("source/FishingAutomation/ai-autopilot/")
                     for name in production)
    game_changes = any(not name.startswith("source/FishingAutomation/ai-autopilot/")
                       for name in production)
    if ai_changes and not python:
        return [], "python_regression_test_missing"
    if game_changes and not csharp:
        return [], "production_regression_test_missing"
    if any(changes_scheduler_source(name) for name in changed) and SCHEDULER_TEST not in csharp:
        return [], "scheduler_regression_test_missing"
    for source, required_test in SCREEN_TESTS.items():
        if source in changed and required_test not in csharp:
            return [], "production_screen_regression_test_missing:" + required_test
    for source, required_suites in CRITICAL_SOURCE_TESTS.items():
        if source in changed:
            for suite in required_suites:
                if suite not in csharp:
                    return [], "critical_source_regression_missing:" + suite

    commands = [["dotnet", "run", "--project", project, "-c", "Release"]
                for project in sorted(projects)]
    # Fixed command arguments, no shell or Codex-supplied command vectors.
    for name in sorted(python):
        commands.append([sys.executable, "-B", "-m", "unittest", "discover",
                         "-s", "tests/ai-autopilot",
                         "-p", PurePosixPath(name).name, "-v"])
    return commands, None
