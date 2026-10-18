# Stage 14 — Read-only CLI evidence in existing AI error context

## Implemented, draft only

This stage enriches the **existing** local runtime-error -> opted-in AI watcher
flow with *bounded, categorical observations* from **CLI requests already
completed by the macro**. It never creates a new CLI request, packet capture,
screen read, game input or automatic recovery. It is not a new real-time
Mobilay event decoder.

The data path is:

```text
Existing MabinogiMobileCli.Finish() — already executed read-only query
   |  (opt-in: user-enabled AiAutopilotHost child actually started)
   v
AiPassiveCliEvidence  — RAM only, 5 known queries x 2 finishes, 5s TTL
   |  (RuntimeErrorUploader existing local error report)
   v
AiRuntimeErrorJournal.Record() — fixed-only annotation in error.txt
   |  (existing Python watcher context_for(), no new paths)
   v
Codex untrusted-log context — existing isolation, real tests, review gates
```

`AiAutopilotHost.Start()` enables the passive buffer only after a
successful watcher process launch. `Stop()`, watcher exit and watcher startup
failure clear/disable it. The disabled default retains no observations.
The buffer has no query/execution delegate, persists nothing by itself and
stores only command enum-like labels, fixed outcomes and UTC completion
timestamps in RAM. Error-time rendering emits at most five status lines
where completion was **before** the error timestamp and at most 5 seconds old.
Two recent finishes per query protect historical evidence from a late
post-error completion without allowing replay from an older watcher
session.

## Approved data and interpretation

Queries: `status`, `get_activity`, `get_altering_works`, `get_items`,
`get_alterable_items`. Private CLI queries (`get_my_info`), arbitrary
commands, action requests, packets, screenshots and raw JSON are ignored.

The existing `ReadOnlyCliObservationBridge.ProjectExistingResult()` reuses
the original production CLI parsing rules *after the original read*; new
processing is opt-in diagnostic computation only. This can distinguish:
- validated CLI status connected from CLI timeout/unavailable;
- verified gathering/activity report vs unknown activity;
- validated altering work running, row-completed-but-receipt-unknown,
  empty works, pending/mixed work rows;
- item list shape only, not quantity delta, and recipe-catalog shape only.

The classification is **not** proof of a gameplay event, successful
ingredient gathering, output receipt, dungeon clear or safe automatic input.
When a CLI parser rejects a response, the diagnostic reports schema
unverified. Unexpected status/error text is converted to a generic fixed
failure; user-controlled strings are not copied.

Only the user's current opt-in **AI runtime watcher** can trigger
collection. On each existing runtime report, `AiRuntimeErrorJournal`
calls `SnapshotForError(report.Time)` and optionally appends a
small allowlisted block to the already-existing **local** `error.txt`.
It never adds fields to the existing optional **GitHub error upload** or
disables its current user settings. The Python watcher already reads and
redacts `error.txt` with an `<untrusted-log>` prompt boundary: no
new AI ingestion permission, image upload or parser was added.

**Important privacy distinction:** the new diagnostic block contains no
user/character/item names, CLI JSON, credentials, addresses or arbitrary
errors. Existing runtime error titles, logs and optional uploads continue
to follow their **pre-existing** privacy behavior; this stage cannot
guarantee those unrelated fields are redacted. Users can turn off AI
monitoring as before.

## Tests and limitations

- `tests/common-observation/Program.cs`: disabled-by-default, lifecycle
  reset, allowed queries only, malicious strings not reflected, 5s TTL,
  no post-error evidence, two-entry history, semantic non-authorization.
- `tests/ai-autopilot/test_passive_cli_context.py`: synthetic local error
  folder proves that the existing Python context ingests the safe block
  as an **untrusted** part of `error.txt` but never opens arbitrary
  private sidecar files.
- Windows CI: the above tests, existing packet and Mobilay diagnostics,
  main program build, Abyss/Altering scheduler regressions, production
  engine regressions. None runs an actual game, AI service or live CLI.

**Not yet completed:** using verified ongoing packet game-state events,
ground-truth ingredient/recipe shortage signals, live error-rate reduction
measurements or actual Codex root-cause improvement. The Stage 13
manual-diagnostic window still does not poll during multi-altering.

No production release, `main` merge, gameplay action, automatic fix
authorization, new background polling or AI upload feature was added.
