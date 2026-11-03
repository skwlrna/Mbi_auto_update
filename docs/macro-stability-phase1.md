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


## Windows PC supervised observation (separate from CI)

This is an **operator-run, read-only** monitor for an already-running
application. The GitHub-hosted CI runner never launches the game. Install a
reviewed candidate on the owner's Windows PC only after the candidate has
passed its safety gates; the draft PR is not a distributed release.

Prerequisites:

1. Keep a clean backup of the currently working program and local durable
   batch records. Do not overwrite the normal installation with CI outputs.
2. Open the approved candidate normally on Windows, then start the chosen
   **existing** crafting, multi-altering or Abyss function manually.
3. Open Windows Task Manager > Details and identify the running macro's **PID**.
   Do not use the MacroWatchdog.exe PID.
4. In a separate Windows PowerShell window, from the repository checkout, run:


    powershell.exe -NoProfile -File .\tools\validation\Invoke-MacroStabilityObservation.ps1 -ProcessId 12345 -DurationMinutes 120

   Replace 12345 with the real macro process PID. The script monitors the
   process and does not start or stop it. If Windows cannot resolve the
   executable directory, supply \`-ApplicationDir 'C:\path\to\installed\macro'\`.
5. It creates a local report under
   \`%LOCALAPPDATA%\MabiAuto\validation\observation-<PID>-<UTC>.json\`.
   No automatic upload, app update, release, game input or screenshot occurs.

Suggested manual validation order (not automatically executed):

| Mode | Initial observation | Extended observation | Evidence to check manually |
| --- | --- | --- | --- |
| Crafting | 15-30 min | 120 min | UI selected recipe, verified output count, blocked materials, F10 stops safely |
| Multi-altering | 15-30 min | 120 min | Each facility lane's work receipts against the durable ledger, F9/F10 sequence |
| Abyss (no gimmicks) | 15-30 min | 120 min | Known screen state, permitted exits/re-entry, no phantom completion or input replay |

A clean report says \`OBSERVATION_ONLY\`, never \`GAME_PASSED\`.
\`REVIEW_REQUIRED\` means stop any ongoing mode **using its existing F10 control**,
retain the existing ledger and review the local log. Do not re-register or
reclaim uncertain work merely because the watchdog detected silence.

The monitor checks only process lifetime, memory, presence/age of the UI
heartbeat, and (if available) new event *codes* written during the window.
It does not read the game, inspect items, identify a character, count crafts,
send key/mouse commands, or make network requests. A quiet, healthy heartbeat
proves only that the UI timer is active; it does not prove actual progression.

### Windows self-test without any game


    pwsh -NoProfile -File .\tests\stability-phase1\Test-WindowsObservation.ps1

The self-test observes its own PowerShell process with a synthetic heartbeat
and confirms normal, stale-heartbeat and missing-heartbeat outcomes. GitHub
Actions runs this test on a hosted Windows runner in the isolated PR gate.

### Known blind spots

- The local event journal may rotate during extended sessions. The report
  aggregates only the latest 4096 event lines within its monitoring window.
- The read-only tool does not have permission to resume an interrupted process.
- Per-facility, output, receipt and Abyss completion correctness must still be
  confirmed by the existing product-specific evidence and operator review.
- The observer cannot detect every input collision or game hang. Distinguish a
  responsive UI from confirmed game progression.
