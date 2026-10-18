# Stage 12 — Actual read-only CLI adapter and multi-altering cross-check (draft)

## Scope and safety

Implemented in `source/FishingAutomation/Telemetry/`:
- `ReadOnlyCliObservationProjection.cs` — bounded, identifier-free diagnostic classifications.
- `ReadOnlyCliObservationCollector.cs` — explicit one-shot adapter with an approved typed query enum and opt-in flag.
- `ReadOnlyCliObservationBridge.cs` — connection to **existing** `MabinogiMobileCli` read-only query methods and production parsers, plus passive `SchedulerRunMonitor.IsRunning` and `ProductionStage` comparisons.

This is a **library API that is not invoked from the production UI, F9, scheduler runner, AI autofix, or game-input path**. The adapter is dormant by default, requires both `CommonObservationEngine(diagnosticsEnabled:true)` and `ReadOnlyCliObservationCollector(explicitlyEnabled:true)`, and performs no background polling. Calls are **only** performed when a future reviewed caller explicitly calls `ReadOnceAsync`; current MabiAuto production behavior remains exactly as before. No new CLI commands, credentials, raw packet decoder, Npcap hook, Mobilay live API or automatic game actions have been added.

## Queries and evidence limits

| Exact existing query | Existing parser used | Current bounded diagnostic result |
|---|---|---|
| `status` | `MabinogiMobileCli` result classification | `Connected` only on valid connected response. `Disconnected` only for explicit CLI disconnect; timeout and CLI unavailable remain **unknown**, not disconnected |
| `get_activity` | `GatheringQueries.ParseActivity` | Positive in-combat/auto-travel/auto-play -> activity active. Positive `IsGathering` -> gathering in-progress. Unrecognized idle/fishing not inferred |
| `get_altering_works` | `AlteringQueries.ParseWorks` with existing strict completed-count and row-schema checks | In-progress if any work runs; all rows `Completed` -> **CLI rows completed only**, not receipt/batch completion; empty/mixed/pending -> unknown |
| `get_items` | `InventoryLootTracker.Parse` with empty requested-name set, strict row validation | only number of inventory rows; no stable/changed, no item delta and no confirmed materials |
| `get_alterable_items` | `AlteringQueries.ParseRecipes` | only recipe row count, no claim that recipe is actionable or that materials were supplied |

The collector emits nothing unless explicitly invoked. Every result is a fixed status, typed `Domain` and `State` (where a valid claim exists), and bounded counts. No recipe names, character IDs, CLI bodies, item names, process paths or credential strings appear in the projector/collector results. The original CLI can still do its existing logging; this adapter never alters or reconfigures that logger.

## Cross-checking local production stages

`ReadOnlyCliObservationBridge.AuditLocalProductionStage` maps **only**
`ProductionStage.Process` -> local `InProgress` and
`ProductionStage.Complete` -> local `Completed`. The independent
`MultiAlteringObservationAudit` yields:
- `local_complete_but_work_running`
- `server_rows_completed_receipt_unknown`
- `same_stage_diagnostic_only`
- `unknown_server_work_state` / `insufficient_cross_evidence`

These are advisory-only. A server row marked Completed does not verify that the output was received or stored. This model cannot replace existing `InventoryLootTracker`, `FacilityLaneState`, `MultiAlteringConsumptionLedger`, the receipt checks, or any input-authority guard. The read-only bridge also provides `ObserveRunningScheduler`, which observes `SchedulerRunMonitor.IsRunning` **only if it actually is true**. A non-running scheduler is not assumed to have completed.

## Failure and privacy safeguards

- `ReadOnceAsync` handles only enum `Status`, `Activity`, `AlteringWorks`, `Items`, `AlteringCatalog`. Unknown values are rejected **before** the query delegate runs.
- The collector performs **no additional retries or job registrations**.
- Only a successful, strictly parsed CLI result can produce a semantic claim. Malformed work count/schema and transient errors fail closed.
- Observations expire after 3 seconds; `CommonObservationEngine` enforces age, source allowlists, replay sequence, conflict detection and no-gameplay-authority invariants.
- A failed query is reported as a fixed classification. No exception body is returned; cancellation propagates without triggering a retry.
- The source bridge is separately compiled by the Windows macro project. Unit tests inject synthetic read-only query delegates; they **do not run the user's game, CLI, Npcap, external application, or private files**.

## Validation

```text
dotnet run --project tests/common-observation/Regression.csproj -c Release
dotnet run --project tests/packet-observation/Regression.csproj -c Release
dotnet run --project tests/packet-receive-probe/Regression.csproj -c Release
dotnet run --project tests/mobilay-export-inspector/Regression.csproj -c Release
dotnet build source/FishingAutomation/FishingAutomation.csproj -c Release -r win-x64
```

Windows CI additionally executes the existing altering/crafting/gathering/loot, scheduler, and full production regressions. No live errors reduced yet; any numerical performance or reliability improvement requires a **subsequent** user-authorized A/B diagnostic comparison. Draft PR #231 only, no main/release/installed macro changes.

## Next reviewed step

A **separately approved diagnostic-only caller** in the existing macro could create an enabled instance, use existing CLI query results where possible, and present only the fixed conflict summaries in an opt-in diagnostics view. It must not insert new background query rates, affect game inputs, or auto-run from F9. Keeping it dormant now protects current multi-altering and ingredient dependency workflows.
