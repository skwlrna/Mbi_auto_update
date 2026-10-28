# F9 post-update real-game acceptance plan (user-operated only)

**Purpose:** The user will execute real-game testing only **after** a future authorized auto-update. This document is an execution checklist, not an authorization to publish or install PR #341. The existing PR remains draft and unmerged; app release and auto-update are excluded from offline validation.

## Prerequisites

1. Confirm the installed version's build commit includes the eventual approved F9 branch merge (do not assume a passing PR CI is installed locally).
2. Work from one Windows user account and the same in-game character/server; use a controllable set of low-cost processing recipes. Do not test with irreplaceable materials.
3. Back up (do not delete) `%LOCALAPPDATA%\\MabiAuto\\multi-altering\\`. Preserve every `fresh-runs\\f9-<batchId>\\batch.json` including its creation time and SHA256, and the main application log.
4. Be ready to stop with F10; record screen and CLI logs without uploading account identifiers or private screenshots to a public PR.
5. Each run starts with a newly user-selected goal. To resume, select the explicit **↻ 이어하기** action, not **F9 새 작업**.

## Required scenarios and observable outcomes

| Scenario | User action AFTER authorized update | Pass conditions | Failure evidence |
|---|---|---|---|
| G01 shutdown | Start 100 units with a multi-work recipe; stop using F10 after 3 new registrations; exit and reopen | Same batchId/100 target/3 confirmed registrations; no re-registration of those 3; remaining registrations only | Old/new batch.json, queue counts, input timestamps |
| G02 stock consumed | Receive 3 works and confirm receipt, sell/use all received output; exit/reopen | Production count remains 3 works; registered missing works = target - confirmed registrations; no false reset | Pre/post stock and journal, receipts |
| G03 outside acquisition | Obtain same output from another source during stop | Outside stock does not count toward F9 produced quantity | Source of external stock, journal counters |
| G04 active queue | Stop while the 7-lane queue contains pending works; reopen | Queue remains the expected size; only free slots are filled; facility receipt is never partial | Lane timeline, queue screens |
| G05 receipt uncertainty | Simulate a stop as near as practicable to receipt (do NOT intentionally cut power or damage files) | A proven before/after receipt can resume; ambiguous pending receipt remains RecoveryRequired; no second blind Space | Pre/post manifest and receipt log |
| G06 update preservation | Stop mid-target; perform later *approved* update; reopen | Byte-exact persisted batch or explicit documented migration; same target/identity/ledger; no duplicate run | Two SHA256 hashes and files |
| G07 rollback preservation | If rollback is required by actual updater failure, inspect original batch afterward | Rollback does not remove or silently rewrite the F9 work journal | Updater logs, before/after hashes |
| G08 Abyss sharing | Process and Abyss schedule each require UI control | No action during exclusive Abyss lease; return only on positive verified world state | Coordinator timestamps, screen proof |
| G09 cancellation | Press F10 during movement or near registration | No further keyboard DOWN/click after cancellation; bounded driver release | Input event trace and video |
| G10 recipe/onsite identity | Try leather/metal/wood and gray-blue onsite/remote facility screens | No wrong facility registration, blind move retries or wrong menu clicks | Screenshot evidence from each condition |
| G11 background resource | Continue a long multi-recipe goal with material consumption | Correct internal-consumption credits, stable 7 slots, no double output credit | Complete batch journal + work list |
| G12 result/finished state | Finish full target and restart | Exact goal reached, no resurfacing as new F9 work; new user-requested run creates new batchId | Manifest terminal state and UI |

## Test verdicts

Each scenario is **PASS** only with input/CLI/ledger evidence. Otherwise label **FAIL**, **NOT REPRODUCED** or **NOT TESTED**. Do not infer a PASS from a screenshot or an expected test name alone. Store private recordings locally.

## Offline readiness already available in PR #341

- CI: N02 persistence, F05 durable consumption, updater success/error/rollback, scheduler/HUD/production engine/transfer OCR.
- Stock volatility cases: output sold/used, outside acquisition, interruption and restart, original target completion.
- Guarded tests: ambiguous receipt/registration and 7-slot ownership remain fail-closed.
- Historical corpus: see `F9_HISTORICAL_CORPUS_CENSUS.md`; 118 multi-altering reports, not all independently replayed.

## Blockers before any approval to release

- The unified `ManagedProductionSceneArbiter.Allows` is a pure policy model and its full use at every real registration/receipt/action boundary has not been proven. Existing fresh-frame guards and bounded inputs are independently present.
- Windows CI and offline frames do **not** substitute for live UI timing, actual F10 key-up behavior, sustained game sessions, or individual replay of private incident screenshots.
- The user explicitly deferred G01-G12 until after an authorized automatic update; those outcomes are **NOT TESTED**, not simulated successes.

Do not merge, publish or trigger auto-update as part of this checklist.
