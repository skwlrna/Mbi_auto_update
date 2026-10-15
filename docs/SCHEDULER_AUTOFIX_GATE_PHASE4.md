# Scheduler auto-fix regression gate — Phase 4

Focus: make existing Codex Autopilot safer and more relevant to scheduler bugs.
This phase does NOT introduce auto game input, auto merging, deployment or
unattended restoration from stale run snapshots.

## What changed

The existing `regression_policy.commands_for` already enforces an allowlist of
executable tests and the requirement for a production source patch. The added
scheduler-specific veto closes a coverage gap: if Codex modifies a critical
scheduler source file, it MUST also modify
`tests/combined-scheduler/Program.cs`. Editing only unrelated tests (e.g.
`tests/altering/Program.cs`) cannot claim verified scheduler recovery.

Existing allowed regression projects are still executed through the same
bounded `dotnet run --project` command list. The AI prompt also asks for
deterministic *positive and veto* cases for scheduler modifications. A patch
with no trustworthy repro/test remains a review case, not a verified fix.

The existing error memory already stores hashed features, suspected error
type, prior failure fingerprints and explicit human feedback. For evidence
bearing an exact `[스케줄러]` (or `[scheduler]`) log tag, classification now
prefers the tentative `scheduler_transition` label over incidental "receipt"
or "CLI" words. This is a heuristic for better related-attempt retrieval,
**not** proof of a root cause or successful game-side fix.

## Safety invariants

- No raw logs or images are added to error memory.
- Failed/offline-only results never become "game validated" automatically.
- Explicit user feedback remains required for `resolved` and `recurred`.
- The existing Autofix-OFF prerequisite, Codex sandboxed worktree, PR draft,
  no-push/no-deploy default and test execution allowlist all remain intact.
- Existing scheduler F10, game input ownership and durable resume/receipt
  confirmation are unchanged.
- Water-path features remain on hold.

## Test and release procedure

`python -m unittest discover -s tests/ai-autopilot -p 'test_*.py' -v`

The established Windows `Check V3 Production Engine` workflow already runs
this Python suite plus main program build and broad regression tests.
Passing CI proves an offline regression gate, **not** live-game recovery.
