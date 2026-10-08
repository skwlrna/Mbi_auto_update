# N02: durable completion for one multi-altering batch

Baseline: `4352384c23800552bbee2193e395e400d78c89ee` (N03 PR #113 merged).
Scope: N02 only. No version/release, game coordinates, OCR conditions, delays,
seven-slot scheduling, facility travel rules, input implementation, or F05 ledger
algorithm is changed.

## Confirmed cause

`StartMultiAlteringAsync` created separate `plan-*.json` checkpoints and invoked
`AlteringAutomation.RunBatchAsync` through `MultiAlteringCoordinator`. After
registration and inventory/receipt verification, `RunCoreAsync` deleted the
individual checkpoint and returned Completed. The coordinator remembered that
item only in its process-local set. On restart, MainForm treated its absent file
as a fresh item and created a new baseline. Mismatching saved plans were also
previously deleted and replaced. Both paths could lose completion evidence.

Before production edits, a deterministic fake registered and received 100 wood
(10 works), then instantiated a new store against the actual temporary multi-plan
path. The assertion that the completed checkpoint still existed failed:

```
N02 reproduced: completed main item checkpoint was deleted before the other batch items finished
```

This was an executed negative baseline on the unmodified source, not a real-game
experiment and not a string-search test.

## Implementation and lifecycle

| File / function | Change |
| --- | --- |
| `AlteringSession.cs` | Optional BatchId, MultiState and CompletedAt fields; existing v1 files remain readable. Store operations are virtual so batch item views use the same automation interface. |
| `MultiAlteringBatchStore.cs / OpenAsync` | One `batch.json` contains the complete roster and full item checkpoints. Validates batch ID, exact character/account/realm context and facility/name/recipe ordinal/yield/target/work count; item order is irrelevant. Holds an exclusive file lease. |
| `MultiAlteringBatchStore / Commit` | Serializes to a sibling temp file, flushes file contents to disk and atomically replaces the manifest. Publishes in-memory state only after success. A write/replace failure poisons further writes in that run and includes the underlying cause in its error. |
| `AlteringPlan.cs / RunCoreAsync` | Refreshes batch item views at entry. Completed items return saved target progress before inventory queries or game input. Multi completion is persisted; single and standalone dependency checkpoint deletion remains unchanged. SaveSession publishes only after saving. |
| `BeginReceiptAsync / ConfirmReceiptAsync` | Marks unfinished main items in the entire facility RecoveryRequired before receipt input. After existing guarded receipt verification, rechecks the empty lane and quantities for all main peers and commits their receipt/completion together. This includes receipts made by the recursive scheduler before subsequent material consumption. |
| `MultiAlteringCoordinator / RunAsync` | Restores completed identities at startup and before each selection. Excludes them from the registration loop; a claimed completion requires durable evidence. Existing one-slot round robin, watchdog and lane policy are preserved. |
| `MainForm.ProductionPage.cs / ApplySavedAlteringSessionSelection`, `MainForm.Altering.cs` | Rehydrates the entire pending roster including completed peers into the UI. A pending batch cannot be bypassed by emptying that roster and starting an automatic single plan; normal single processing without a pending batch remains unchanged. |
| `MainForm.MultiAltering.cs` | Wires the batch views and both main/dependency receipt hooks. Completed producers leave the existing consumption observer; no credits are fabricated. Restores completed progress without inventory-based reopening. Filesystem work runs off the hotkey/UI thread. |
| `MultiAlteringDependencyScheduler.cs` | Forwards the same receipt hooks through both the existing-lane collector and recursive work automation. Its scheduler and own session lifetime otherwise stay unchanged. |
| `AssemblyInfo.cs`, test projects, `ci.yml` | Windows N02 tests reference the compiled app; portable tests link the same source. Existing suites/expectations remain intact. The N02 branch push also runs Windows CI before PR creation. |

Item states: NotStarted, InProgress, Completed, RecoveryRequired. Registration
count alone never proves completion. Completed evidence is immutable even when
inventory later decreases because an output is used as an intermediate material.
The manifest itself is the authoritative item store; main item views do not
independently write/delete plan files, avoiding a two-file completion transaction.

Batch states: Active -> Completed -> Closed. After all items have confirmed
completion, commit Completed first, clean owned dependency files, clear the UI
plan, then acknowledge Closed. The terminal manifest is retained through cleanup
and is replaced only when an acknowledged new batch is created. A restart with
Completed performs cleanup/plan clearing with no game input. A crash before whole
completion still restores the individual Completed entries. New identical item
plans after Closed receive a new batch ID and fresh baselines.

Managed visual receipts now commit RecoveryRequired at the final irreversible
receive-Space boundary, **after** on-site movement, two-frame blue prompt/OCR
checks, and the 400ms stabilization/recheck. A failed *pre-input* screen check
cannot poison a resumable session because no receipt was yet attempted.
The zero-wing wrapper passes this same boundary through its pre/post currency
verification; an unknown screen without the boundary contract retains the
conservative early marker instead of assuming no input.

Once the receive boundary is entered, an interrupted input, failed quantity
verification or failed persistence leaves RecoveryRequired and stops without
registration. A native send that may have occurred is never automatically
retried. F10 callbacks do no filesystem work and N03 input protection remains.
An IO operation already blocked in the OS cannot be forcibly terminated, but its
worker does not block hotkey delivery and cancellation blocks subsequent inputs.

## Old records and conflicts

Legacy v1 records are never reset/deleted. A **narrow automatic upgrade**
is permitted only when *every selected plan* has an exact stable-key
`plan-<hash>.json` checkpoint, all registered/pending/existing works and
internal credits are zero, its output count still equals its original baseline,
its complete character/account/realm identity and plan match exactly, and there
are no selected-facility live works, orphan temp files or dependency sessions.
The saved roster is restored read-only in the UI before validation. After
successful verification all zero-work sessions are incorporated in one durable
manifest. Original v1 files remain untouched until the batch finishes; cleanup
moves them into `legacy-archive/<batch-id>/`, rather than deleting them.

Any nonzero, numeric index, missing/deleted completed peer, changed inventory,
unknown identity, corrupt file or uncertain legacy record remains RecoveryRequired
with no autonomous input. The single-start route also blocks incomplete legacy
records instead of treating the absent `batch.json` as a fresh selection.
Existing single-processing v1 restoration remains unchanged. Native/OS failure,
inaccessible manifest or orphan temp fails closed. There is no unsafe reset button
for an unresolved older batch.

## Deterministic verification

Windows (compiled production assembly, fake transport/game only):

```powershell
dotnet run --project tests/n02-resume/Regression.csproj -c Release -r win-x64
dotnet run --project tests/n03-input/Regression.csproj -c Release -r win-x64
```

The N02 executable reports 44 named cases (39 original + five targeted boundary/migration cases). It uses unique temporary directories,
actual file persistence and fresh store/automation/coordinator objects. It never
opens a game window or loads the Interception driver.

| Required scenario | Coverage |
| --- | --- |
| A | Wood 100 complete, steel 4/10 works, rice 3/10; cancellation, dispose/reopen, restore partial counters and total progress, zero extra wood registrations. |
| B | Restart immediately after durable item completion; completed automation returns without an inventory query. |
| C | Interrupted after the receive-input journal, after input, after receipt verification/read, during write and replacement; no false success or automatic registration. A separate read-only OCR/preflight failure before the irreversible input boundary resumes safely without new registrations. |
| D | Wood decreases from 100 to 40 after completion; restart continues remaining plans with zero wood registrations and no invented consumption credit. |
| E | Write/replace/read faults, damaged/null/invalid-version files; original protected and same-run fail closed. |
| F | All three items complete, terminal record committed, dependency cleanup, plan-clear acknowledgement and fresh batch identity. |
| G | Reordered plans restore by facility/name/recipe identity. |
| H | Same name/different ordinal, target, yield, facility, name and each identity field conflict. |
| I | Original unsupported v1 stable/numeric records still fail closed. New positive upgrade requires every plan's exact zero-work stable file, same output baselines, identity and empty facility queue; preserves/archives originals. Changed counts/live works/missing completed peer continue to stop. Orphan temp stops. |
| J | Whole completion committed; termination after only one dependency file is deleted; restart does cleanup with zero registration. |
| K | New equal wood 100 batch after acknowledged completion: different batch ID, ten normal new registrations. |
| L | A/B/A/B/A/B/A seven-slot mix, a completed slot while others run, no partial receipt, two whole-lane receipts, one Fresh queue directive then Reuse; completed A is never registered again. |
| Additional | Recursive collector persists main peer completion before child use; successful single checkpoint deletion; cancellation before save/receipt; cancellation while checkpoint IO is deliberately blocked; whole-completion replace failure; concurrent instance lease; pending UI roster lifecycle; forged item batch-ID conflict. |

## Validation classification and limits

- **Confirmed:** baseline loss of item evidence, actual negative reproduction,
  no code changes to input/N03, F05 ledger, fixed screen coordinates or policies.
- **Passed locally:** N02 44, altering 238, crafting 75, gathering 64, portable N03
  55 (Windows adds three STA tests for 58). Windows-target production cross-build
  via .NET 8 MSBuild succeeds with zero errors. The host's dotnet CLI sometimes
  fails retrieving Process.StartTime; direct MSBuild/Roslyn execution was used as
  appropriate and is not claimed as execution on Windows.
- **Windows CI:** full existing workflow plus N02 44 and N03 58; final run links
  and results are recorded in the PR and final response.
- **Conditional:** atomic replacement/Flush(true) relies on a functioning local
  filesystem. Physical disk/OS corruption, manual record removal, or a native IO
  hang cannot be fully addressed by managed code. RecoveryRequired and legacy
  records require review/reconciliation; this PR supplies no unsafe reset button.
- **Not Tested:** real game, real Windows desktop UI plan interaction, installed
  Interception driver, physical F10 latency and power-loss/filesystem fault tests.
- **Remaining separate defect:** F05 consumption-ledger persistence/idempotency is
  not fixed. N02 protects confirmed Completed items; unresolved in-progress
  quantity/consumption ambiguity still stops instead of inventing credits.
