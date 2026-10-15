# MabiAuto Scheduler Supervision — Phase 2

This phase strengthens the *existing* scheduler, not the on-screen water-path
flow. It does not introduce any independent game-input worker or automatic
recovery. Existing MacroWatchdog still monitors UI heartbeat and the
MultiAlteringWaitWatchdog still enforces verified processing countdown rules.

## Observable scheduler state

The scheduler records an optional, **local-only** JSON snapshot in
`%LOCALAPPDATA%\MabiAuto\scheduler-runtime.json`. This is diagnostic state,
**not** a resume ledger. It contains an ephemeral run ID, process ID, flags for
which stages were selected, the current phase, UTC timestamps of phase changes
and UI heartbeat, the last observed activity log, and the stop/outcome flag.
No character name, selected recipe, raw logs, screenshots, credentials, or
game input is written to this snapshot.

Phases: preparing, resume_selection, crafting, handoff, altering, stopping,
completed, cancelled, failed.

When an active crafting/altering phase has had no relevant logged activity
for 15 minutes, MabiAuto adds exactly one advisory to the ordinary runtime log:
the user should check whether this is a legitimate wait. This is **not proof
of a stall**. It never cancels, restarts, switches stages, calls AI, or sends
an alert itself. The window resets after a relevant activity log or transition.
Manual resume selection and handoff are excluded to avoid false stall reports;
handoff retains its existing time-bound ownership check.

## Controls and failure safety

- The existing start guard remains, with a second atomic/locked start gate
  at the scheduler entrance. A rejected second attempt cannot launch a worker.
- F10/StopAll mark `stopping`, cancel existing tokens, and never run a next
  stage. Late completion after user stop remains `cancelled`.
- Closing the macro stops its existing child watcher (unchanged). Nothing
  automatically re-launches the app.
- Failure to persist diagnostics is non-blocking: normal scheduler execution
  is not interrupted by lack of write permission.
- The next process does not restore or auto-start based on a stale snapshot.
  Existing explicit consent / durable batch-resume safety stays unchanged.
- All scheduler/game UI automation is still exclusively owned by the existing
  verified crafting/multi-altering engines.

## Verification

Deterministic unit-style regressions added to
`tests/combined-scheduler/Program.cs`: guard, local snapshot,
two-hour manual prompt, one-shot advisory, progress reset, handoff exclusion,
F10 / late completion, startup without recovery, and optional storage failure.

Windows CI build, combined scheduler tests, production UI and normal
regressions must pass before this should be included in a production release.
A green offline run does not prove a game-side stall is correctly identified.
