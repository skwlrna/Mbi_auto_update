#!/usr/bin/env python3
"""Read-only gate for a previous isolated AI patch; never invoke Codex."""
from pathlib import Path
import hashlib
import sys
import autopilot as a
from abyss_recovery_gate import TARGET, audit_path


def review() -> int:
    c = a.config()
    results = a.expand(str(c['results_dir']))
    workroots = a.expand(str(c['worktrees_dir']))
    files = sorted(results.glob('*/result.json'), key=lambda p:p.stat().st_mtime, reverse=True) if results.is_dir() else []
    matching = []
    for file in files:
        try:
            record = a.read_json(file)
            if str(TARGET).replace('\\', '/') in record.get('changed_files', []):
                matching.append((file, record))
        except (OSError, ValueError, TypeError):
            continue
    if not matching:
        # Fresh installations have no prior AI patch to review. Watch safely,
        # and audit any new patch before it can ever be applied.
        print('NO_PRIOR_ABYSS_PATCH: no existing AI patch to audit; live deployment remains disabled')
        return 0
    file, record = matching[0]
    print('Result:', file)
    print('Prior status:', record.get('status', 'unknown'))
    print('Recorded tests:', len(record.get('tests', [])))
    if record.get('status') != 'tests_passed_review_required' or not record.get('tests') or not all(t.get('ok') is True for t in record['tests']):
        print('REVIEW_BLOCKED: prior automated tests were not all verified successful')
        return 2
    worktree = Path(record.get('worktree') or '')
    if not worktree.is_dir() or worktree.is_symlink() or not worktree.resolve().is_relative_to(workroots.resolve()):
        print('REVIEW_BLOCKED: isolated worktree path not valid')
        return 2
    if record.get('repair_kind') == 'unique_selected_dungeon_v123':
        expected = record.get('source_sha256') or {}
        if sorted(expected) != sorted(record.get('changed_files', [])):
            print('REVIEW_BLOCKED: reviewed source hash manifest incomplete')
            return 2
        for name, checksum in expected.items():
            path = worktree / name
            if not path.is_file() or path.is_symlink() or hashlib.sha256(path.read_bytes()).hexdigest() != checksum:
                print('REVIEW_BLOCKED: reviewed source content changed:', name)
                return 2
        if len(record['tests']) < 4:
            print('REVIEW_BLOCKED: dedicated C# regression test result missing')
            return 2
    ok, errors = audit_path(worktree)
    if not ok:
        print('REVIEW_BLOCKED: Abyss recovery source-contract checks failed')
        for problem in errors:
            print(' -', problem)
        print('No patch was changed, merged, or deployed.')
        return 2
    print('SOURCE_CONTRACT_PASS: prior patch passed static inspection only')
    print('LIVE_GAME_VALIDATION_REQUIRED: do not auto-merge or deploy.')
    return 0


if __name__ == '__main__':
    sys.exit(review())