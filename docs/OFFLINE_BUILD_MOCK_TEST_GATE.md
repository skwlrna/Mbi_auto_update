# Offline build and mock test gate

The workflow `.github/workflows/offline-macro-gate.yml` supplements the
existing `Check V3 Production Engine` workflow; it does not replace it.

## When it runs

- On pull requests targeting `main`
- On pushes to `main`
- On manual `workflow_dispatch`

## What gets tested

1. **Ubuntu / deterministic mock scenarios:** compile a standalone .NET 8
   harness against four real, side-effect-free production policies. Feed
   synthetic game-window, input-readiness, inventory, altering-screen and
   Abyss-exit observations. Check fail-closed outcomes. Run existing
   `tests/start-preflight` regressions.
2. **Windows / compiled application:** restore and compile
   `FishingAutomation.csproj` and `MacroWatchdog.csproj` for `win-x64`.
3. **Windows / offline regressions:** run existing crafting, altering, and
   input-cancellation suites. A failing command fails the workflow.

All steps use GitHub-hosted ephemeral runners. Checkout does not persist
credentials; workflow token has `contents: read` only.

## Explicit non-goals and remaining gates

- Does **not** open the real game, inspect a live game window, send real
  keyboard/mouse input, or require the owner's Windows PC.
- Does **not** publish a GitHub Release, update installed files, upload an
  executable, generate a game-test attestation, or trigger a deployment.
- Does **not** imply real-game compatibility. A later separate, user-approved
  Windows PC validation is required before declaring live-game success.
- The preexisting main CI and release/rollback policies remain authoritative.
  Do not interpret a green mock run alone as permission to release.
- To make this a mandatory merge gate, configure GitHub branch protection /
  repository rules to require both checks under **Offline Build and Mock Test
  Gate** (and keep the existing production CI check required).

## Run locally with .NET 8

```powershell
dotnet run --project tests/offline-simulation/OfflineSimulation.csproj -c Release
dotnet run --project tests/start-preflight/Regression.csproj -c Release
```

Windows-only compilation and regressions in the workflow are run using the
GitHub-hosted Windows 2022 environment.

## How to interpret results

- **Deterministic mock scenarios failed:** a real policy changed its decision
  for one of the synthetic states. Inspect the first `OFFLINE_SIMULATION_FAIL`
  message before changing production behavior.
- **Windows build failed:** inspect the .NET compiler/restore error; this is
  not a game runtime failure.
- **Offline regressions failed:** inspect the named regression suite; no live
  input was sent.

Nothing is automatically merged or deployed by this workflow.
