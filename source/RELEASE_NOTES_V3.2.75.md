# Mabi Auto V3.2.75

This is an explicitly requested personal-use **auto-update rollout without completed live-game verification**. The user will run real-game tests *after* installing this version.

## F9 multi-processing improvements
- Unified scene classification and fresh input-frame guards for HUD, six-card hub, food/armor menus, facility screens, travel overlay and receipt; fail closed on ambiguous screen, CLI movement, F10 and lane conflicts.
- F9 saved-target resume uses confirmed registration/receipt work journal, not fluctuating bag inventory. Sale/use of output and unrelated same-item acquisition do not silently reset or inflate goal progress. Strict legacy and unresolved receipt/registration safeguards remain.
- Stronger resume preflight compares complete durable manifest after the game queue is read, preventing state-change races.
- Reduce **managed F9 only** two unconditional **550ms** sleeps after processing-tab and facility-card clicks: start positive hub/facility polling immediately, with original overall timeout budgets and exact facility two-frame gates intact. Automatic single-altering path unchanged. Capture milliseconds for hub/facility scene handoffs.
- 41 historical incident-sourced conservative offline safety checks pass, with a full inventory of 238 historic summaries (118 multi-altering, 77 not separately replayed). This is **not** proof that all incidents are resolved in the game.

## Verification and rollout
- Required same-commit PR CI: Managed K HUD, Abyss + Altering Scheduler, V3 Production Engine, Material Transfer OCR; later **merged-main** V3 CI required again.
- N02 resume, F05 durable consumption, F10/N03, updater success/rollback, Windows package, source and SHA256 checks required before public release.
- Release workflow validates a single current-main SHA, app and EXE `V3.2.75`, required F9 source/test guards, native Interception file, Windows Lite ZIP, source, check-sum, rollback script, app startup smoke. Never overwrite previous `V3.2.74` release.
- Actual game acceptance plan: `tests/altering-screen-integration/F9_POST_UPDATE_GAME_ACCEPTANCE.md`; results not yet available. Users must back up F9 AppData records and avoid new-task creation when resuming.
- Existing automatic-updater installs only through the application's safe update operation, not by remote forced input. Previous-version rollback helpers included.
