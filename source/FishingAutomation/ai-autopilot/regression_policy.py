"""Allowlisted executable regression suites for AI-generated repair tests.

A successful build without a modified, registered regression test is REVIEW,
not a verified fix. Existing test projects only; no arbitrary test commands.
"""
from pathlib import Path, PurePosixPath

REGISTERED_TESTS = frozenset({
    "tests/altering/Program.cs",
    "tests/n02-resume/Program.cs",
    "tests/f05-consumption/Program.cs",
    "tests/combined-scheduler/Program.cs",
    "tests/loot-retry/Program.cs",
    "tests/retry-flow/Program.cs",
    "tests/gathering-diagnostic/Program.cs",
    "tests/inventory-loot/Program.cs",
    "tests/n03-input/Program.cs",
    "tests/cli-readonly/Program.cs",
})



# Critical scheduler source changes require the real combined-scheduler
# regression suite. An unrelated but registered test cannot authorize a
# scheduler repair. This list is deliberately narrow; arbitrary code edits
# must still satisfy the existing allowlist and global regression checks.
SCHEDULER_TEST = "tests/combined-scheduler/Program.cs"
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


def commands_for(changed: list[str], worktree: Path
                 ) -> tuple[list[list[str]], str | None]:
    modified_tests = [name for name in changed if name.startswith("tests/")
                      or name.startswith("changes/tests/")]
    if not modified_tests:
        return [], "regression_test_missing"
    if any(name not in REGISTERED_TESTS for name in modified_tests):
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
    return [["dotnet", "run", "--project", project, "-c", "Release"]
            for project in sorted(projects)], None
