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
    return [["dotnet", "run", "--project", project, "-c", "Release"]
            for project in sorted(projects)], None
