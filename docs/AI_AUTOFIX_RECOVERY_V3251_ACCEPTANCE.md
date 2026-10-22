# V3.2.51 AI autofix recovery — Windows PC acceptance

Scope: Draft PR #271 only. Do not replace installed V3.2.51, merge PR, or publish an updater before real PC and game acceptance.

## Preconditions

1. Extract the unverified Windows candidate artifact from the latest successful PR #271 Actions run into a separate test directory, never over the installed software.
2. Stop the AI watcher using the application's Stop control. Confirm any detached Codex process has ended before reclaiming its checkout.
3. Independently back up the user MabiAutoAI directory and original incident logs; check free disk space for verified recovery ZIPs (maximum source payload 512 MiB per workspace).
4. Open PowerShell in the isolated candidate package root, which contains release\ai-autopilot\autopilot.py.

## Read-only commands: run these first

    py -3 -B .\release\ai-autopilot\autopilot.py --reclaim-worktrees
    py -3 -B .\release\ai-autopilot\autopilot.py --reclaim-worktrees --include-legacy
    py -3 -B .\release\ai-autopilot\autopilot.py --verify-recovery-archives

Both inventory commands only report eligible and protected IDs and per-ID reasons. They do not remove data, run Codex, or interact with the game. Legacy mode relaxes missing historic metadata only and does not permit active/in-progress, unknown Git owner, untrusted origin, or mismatched identity. The third command independently checks existing recovery archives without extraction.

## Explicit worktree recovery: only after checking the inventory and independent backup

    py -3 -B .\release\ai-autopilot\autopilot.py --reclaim-worktrees --execute-reclaim

If historic records are missing, and ONLY after reviewing each listed ID and evidence:

    py -3 -B .\release\ai-autopilot\autopilot.py --reclaim-worktrees --include-legacy --execute-reclaim

Recovery must hold the same exclusive watcher lock as active AI monitoring. Each eligible checkout is saved with edits, build artifacts, available incident evidence and results to a uniquely named SHA-256 verified ZIP. It checks the bytes again before asking Git to remove the registered worktree. It never runs git clean, alters main, overwrites an archive or deletes original log directories. On verification/registration failure it stops and keeps the checkout.

Afterward read %USERPROFILE%\MabiAutoAI\autopilot-results\worktree-recovery-latest.json and run --verify-recovery-archives again. Do not force-delete protected checkouts to achieve an artificial 0 count.

## Real Codex and real-game acceptance (NOT part of CI)

1. In the isolated candidate run: py -3 -B .\release\ai-autopilot\autopilot.py --pipeline-check. This is an offline prerequisite check, not Codex execution.
2. With watcher stopped and errors backed up, authorize one real Codex analysis/edit for a selected unresolved incident. Check pinned source access and meaningful production source AND registered test changes, or explicit code_unchanged.
3. Verify actual host build, deterministic regression results, exact reason classification, saved archive integrity, and reclaimed workspace. A diff or Draft PR alone is not verified repair success.
4. Test game UI/nest interactions separately, recording real outcomes. Maintain V3.2.51 rollback.
5. Do not release until both real PC Codex and real-game acceptance have been performed and reported separately.

Evidence to preserve: redacted inventory counts/reasons, recovery summary, archive verification summary, one actual Codex repair result with host tests, and separate real-game status. Never paste raw private logs, local secrets, or identifying Windows paths by default.
