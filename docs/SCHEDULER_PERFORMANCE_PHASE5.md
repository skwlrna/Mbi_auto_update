# Scheduler Performance Baseline — Phase 5

This update adds **measurement before gameplay optimization**. Existing
scheduler order, read-only resume verification, 1–30 second processing wait
intervals, full-batch collection, verified Abyss/altering input handoff, and
F10 cancellation are deliberately unchanged. Optimizing travel/recipe order
or polling thresholds without game-side measurements can cause duplicates or
premature UI transitions, so there is no speculative routing change here.

## Observability

A phase duration sample is recorded on each scheduler phase transition:
`preparing`, `resume_selection`, `crafting`, `handoff`, `altering`,
`stopping`. When a run ends, its outcome is `completed`, `cancelled` or
`failed`. Duration and status are read-only diagnostics: "completed" means
the local runner concluded normally, not independent game-state validation.

Each run publishes a compact summary in the existing scheduler log with
total, resume-selection, crafting, handoff and altering elapsed times.

Files (under the current Windows user profile):
- `%LOCALAPPDATA%\MabiAuto\scheduler-runtime.json`
  now includes live `phaseElapsedMilliseconds`, even before completion.
- `%LOCALAPPDATA%\MabiAuto\scheduler-performance.json`
  stores at most **30 recent outcomes**, each with run ID, process ID,
  endpoint UTC timestamp, total duration and per-phase durations.
  No character names, recipes, screenshots, tokens, raw game logs, or
  game input actions are stored.

Runtime snapshot writes without a transition have been reduced from every
15 seconds to every **30 seconds** to reduce diagnostic I/O. The independent
UI MacroWatchdog heartbeat runs on its existing cadence and is untouched.
Forced writes on starts, transitions, F10 and completion are still immediate.

Corrupted/unwritable performance storage is non-blocking, and stale history
never authorizes automatically starting or continuing a batch.

## Next optimization decisions

Once representative live scheduler sessions exist, compare per-phase latency
and completion/cancel/failure counts. Only then decide whether targeted
read-only polling or input-owner handoff changes improve measured end-to-end
latency. No throughput speedup is claimed without those measurements.

## Regression coverage

`tests/combined-scheduler/Program.cs` verifies phase durations, no disk
rewrite before 30 seconds, final elapsed values, F10 cancellation, corrupted
history resilience, 30-run retention, privacy and no-auto-restart guards.

The current Windows CI runs the combined suite and production regressions.
Passing offline CI does not establish higher runtime success rates.
