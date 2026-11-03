# Macro stability phase 1: crafting / multi-altering / Abyss

Scope: the existing .NET 8 WinForms app, its combined scheduler, and existing
crafting, multi-altering and Abyss paths. Abyss gimmick/room automation is
explicitly outside this change. Fishing and other unrelated modes keep their
existing implementation.

## Added behavior

- A UI start gate rejects another owner while an earlier crafting, altering,
  Abyss or scheduler run has not actually released its lease.
- A dedicated multi-altering run is adopted by its existing live worker flag;
  a combined scheduler remains the parent owner across crafting, altering and
  optional Abyss excursions. Existing verified mode-specific input protections
  are unchanged.
- The local UI watchdog identifies 15 minutes without *relevant activity log*.
  It cannot infer game-side progress/completion from those logs.
- A transient quiet warning is cleared after fresh local activity at most twice
  per session. Further recurrence is marked "ReviewRequired". This is
  **observation recovery**, NOT re-clicking, restarting, game reconnecting, or
  durable receipt/quantity reconciliation.
- F10 marks StopRequested, and later logs cannot re-arm the run. Two idle UI
  observations are required to release the lease after a run. F10 still cancels
  the existing production worker; no new input driver or cancellation stack.
- Lightweight JSONL diagnostics at
  %LOCALAPPDATA%\MabiAuto\macro-stability-events.jsonl, rotated at ~1 MiB,
  contain only time, mode, state, event code and bounded counters.

## Safety boundaries

1. Never use unknown packets, inferred inventory counts or noisy activity
   messages to authorize game input.
2. Never auto-repeat crafting, registration, receipts, Abyss exit or re-entry.
   Existing verified, mode-specific recovery paths remain in control.
3. Existing durable multi-altering ledger, F10 behavior, and Windows update/
   release pipelines are unchanged.
4. No live game tests are performed by this PR. CI workers do not run the game
   and the workflow does not create an executable artifact or publish releases.
5. UI-level input ownership does not replace lower-level native input locks;
   external processes and direct screen helpers are not serialized by this
   new supervisor.

## Checks

Run without a game window or Interception:

  dotnet run --project tests/stability-phase1/StabilityPhase1.csproj -c Release

The deterministic harness tests 300 virtual runs across the three target modes,
including start rejection, owner conflicts, F10, quiet warnings, bounded
observation recovery, and eventual lease cleanup. GitHub Actions additionally
builds the Windows application and the watchdog, then runs the pre-existing
crafting/altering/input-safety regression suites.

This branch is proposed as a reviewable PR. Do not merge or release based on
mock tests alone; run a separate Windows UI/game validation first.
