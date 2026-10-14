#!/usr/bin/env python3
"""Fail-closed, read-only SOURCE-CONTRACT gate for Abyss selection recovery.

This cannot substitute for live-game/UI tests. It catches several unsafe code
patterns that a successful .NET build will not detect, including selecting the
first match from multiple possible dungeon targets.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re
import sys

TARGET = Path('source/FishingAutomation/dungeon/ScenarioEngine.AbyssMenu.cs')


def selection_body(source: str) -> str | None:
    start = source.find('TryRecoverAbyssEntryScreenAsync(')
    if start == -1:
        return None
    # Only inspect this method, not an unrelated recovery branch elsewhere.
    next_method = re.search(r'\n\s*private\s+(?:async\s+)?(?:Task|bool|int|void|static)\b', source[start + 35:])
    end = start + 35 + next_method.start() if next_method else len(source)
    method = source[start:end]
    marker = re.search(r'if\s*\(\s*await\s+VerifyAbyssSelectionScreenAsync\s*\(\s*ct\s*\)\s*\)', method)
    if marker is None:
        return None
    brace = method.find('{', marker.end())
    if brace < 0:
        return None
    depth = 0
    for pos in range(brace, len(method)):
        if method[pos] == '{':
            depth += 1
        elif method[pos] == '}':
            depth -= 1
            if depth == 0:
                return method[brace+1:pos]
    return None


def audit_source(source: str) -> list[str]:
    """Return evidence-based source-contract failures, not success claims."""
    errors: list[str] = []
    branch = selection_body(source)
    if branch is None:
        return ['verified_selection_screen_branch_missing']
    branch_clean = re.sub(r'//[^\n]*', '', branch)
    if re.search(r'FindDungeonStepIndex\s*\(\s*"abyss_selected_dungeon"', branch_clean):
        errors.append('placeholder_step_lookup_may_return_zero')
    if not re.search(r'_scenario\.Steps', branch_clean):
        errors.append('runtime_scenario_steps_not_used')
    if not re.search(r'_resumeStepIndex\s*=', branch_clean):
        errors.append('resume_step_not_set')
    if re.search(r'_resumeStepIndex\s*=\s*(?:0|1|2|3|4|5)\s*;', branch_clean):
        errors.append('fixed_resume_index')
    if re.search(r'ClickClientPoint\s*\(|PressKey\s*\(', branch_clean):
        errors.append('selection_recovery_performs_unverified_input')
    # The reviewed implementation delegates target counting to an audited
    # policy and has separate executable C# zero/one/many regression tests.
    if 'AbyssMenuEntryPolicy.UniqueDungeonSelectionStepIndex(_scenario.Steps)' in branch_clean:
        if re.search(r'FindIndex\s*\(|abyss_dungeon_', branch_clean):
            errors.append('mixed_selector_strategies')
        if not re.search(r'_resumeStepIndex\s*=\s*AbyssMenuEntryPolicy\.UniqueDungeonSelectionStepIndex\(', branch_clean):
            errors.append('unique_selector_result_not_used')
        if 'return true;' not in branch_clean:
            errors.append('recovery_success_not_explicit')
        return errors
    # Recovery must explicitly fail closed if no matching step exists.
    if not re.search(r'(?:<\s*0|==\s*-1|\.Count\s*!=\s*1|\.Length\s*!=\s*1|\.Single\s*\()', branch_clean):
        errors.append('missing_not_found_fail_closed_guard')
    if not re.search(r'\bthrow\b', branch_clean) and '.Single(' not in branch_clean:
        errors.append('missing_failure_exception')
    # Several allowable target literals plus FindIndex is not a unique selection.
    # One scenario could contain multiple such steps, so first-match is unsafe.
    literals = re.findall(r'"(abyss_[A-Za-z0-9_]+)"', branch_clean)
    candidates = {v for v in literals if v.startswith('abyss_dungeon_') or v == 'abyss_selected_dungeon'}
    first_match = bool(re.search(r'\bFindIndex\s*\(', branch_clean))
    unique_enforced = bool(re.search(r'(?:\.Count|\.Length)\s*(?:!=|==)\s*1\b|\.Single\s*\(|\bSingleOrDefault\s*\(', branch_clean))
    # A comparison against one dynamically captured selected id is also safe;
    # avoid hardcoding presumed selector variable names.
    chosen_id_check = bool(re.search(r'\.Target\s*,?\s*(?:_selected|_configured|selectedDungeon|chosenDungeon)', branch_clean))
    if first_match and len(candidates) > 1 and not (unique_enforced or chosen_id_check):
        errors.append('ambiguous_first_match_from_multiple_dungeons')
    return errors


def audit_path(worktree: Path) -> tuple[bool, list[str]]:
    base = worktree.resolve()
    target = base / TARGET
    if target.is_symlink() or not target.is_file() or not target.resolve().is_relative_to(base):
        return False, ['source_file_missing_or_unsafe']
    try:
        source = target.read_text(encoding='utf-8-sig')
    except (OSError, UnicodeError):
        return False, ['source_file_unreadable']
    errors = audit_source(source)
    branch = selection_body(source)
    if branch is not None and 'AbyssMenuEntryPolicy.UniqueDungeonSelectionStepIndex(' in branch:
        policy_path = base / 'source/FishingAutomation/dungeon/AbyssMenuEntryPolicy.cs'
        if policy_path.is_symlink() or not policy_path.is_file() or not policy_path.resolve().is_relative_to(base):
            errors.append('unique_selection_policy_missing')
        else:
            policy = policy_path.read_text(encoding='utf-8-sig')
            if not all(fragment in policy for fragment in (
                'UniqueDungeonSelectionStepIndex(IReadOnlyList<ScenarioStep> steps)',
                '"abyss_selected_dungeon"', 'StringComparison.OrdinalIgnoreCase',
                'if (index >= 0)', 'if (index < 0)', 'throw new InvalidOperationException',
                'return index;',
            )):
                errors.append('unique_selection_policy_contract_missing')
    return not errors, errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--worktree', required=True)
    args = parser.parse_args()
    ok, problems = audit_path(Path(args.worktree))
    if ok:
        print('ABYSS_SOURCE_CONTRACT_PASS (static review only; live behavior NOT tested)')
        return 0
    print('ABYSS_SOURCE_CONTRACT_BLOCKED (live behavior NOT tested)')
    for problem in problems:
        print(' -', problem)
    return 2


if __name__ == '__main__':
    sys.exit(main())