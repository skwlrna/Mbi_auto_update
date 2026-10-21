"""Allowlisted executable regression suites for AI-generated repair tests.

A successful build without a modified, registered regression test is REVIEW,
not a verified fix. Existing test projects only; no arbitrary test commands.
"""
from pathlib import Path, PurePosixPath
import re
import subprocess

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


def _trusted_existing_suite(file: str, worktree: Path) -> bool:
    """Reconcile a missing registry entry only from committed HEAD files."""
    if not re.fullmatch(r"tests/[a-z0-9-]{1,80}/Program[.]cs", file):
        return False
    project = str(PurePosixPath(file).parent / "Regression.csproj")
    for name in (file, project):
        target = worktree / name
        if target.is_symlink() or not target.is_file():
            return False
        try:
            result = subprocess.run(
                ["git", "-C", str(worktree), "cat-file", "-e", "HEAD:" + name],
                capture_output=True, timeout=10, shell=False, check=False)
        except (OSError, subprocess.TimeoutExpired):
            return False
        if result.returncode != 0:
            return False
    return True


def commands_for(changed: list[str], worktree: Path
                 ) -> tuple[list[list[str]], str | None]:
    modified_tests = [name for name in changed if name.startswith("tests/")
                      or name.startswith("changes/tests/")]
    if not modified_tests:
        return [], "regression_test_missing"
    # Reject newly introduced suites; trust pre-existing pinned-HEAD tests.
    if any(name not in REGISTERED_TESTS and
           not _trusted_existing_suite(name, worktree) for name in modified_tests):
        return [], "regression_test_suite_not_registered"
    projects = set()
    for name in modified_tests:
        project = str(PurePosixPath(name).parent / "Regression.csproj")
        if not (worktree / project).is_file():
            return [], "regression_test_project_missing"
        projects.add(project)
    if not any(name.startswith("source/") for name in changed):
        return [], "production_source_patch_missing"
    if any(changes_scheduler_source(name) for name in changed) and SCHEDULER_TEST not in modified_tests:
        return [], "scheduler_regression_test_missing"
    for source, required_test in SCREEN_TESTS.items():
        if source in changed and required_test not in modified_tests:
            return [], "production_screen_regression_test_missing:" + required_test
    for source, required_suites in CRITICAL_SOURCE_TESTS.items():
        if source in changed:
            for suite in required_suites:
                if suite not in modified_tests:
                    return [], "critical_source_regression_missing:" + suite
    return [["dotnet", "run", "--project", project, "-c", "Release"]
            for project in sorted(projects)], None
