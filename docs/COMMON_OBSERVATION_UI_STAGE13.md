# Stage 13 — Explicit-click, read-only game-state diagnostic window

## What is now wired

The actual default MabiAuto UI is the reference dashboard, NOT the
unused legacy layout. Its **로그 / 진단** navigation opens the existing log
panel, built by `MainForm.BuildLogPanel`. Stage 13 adds a visible
**게임 상태 진단** button to that log panel header. Clicking it opens a
separate modeless WinForms window. **Opening the window makes no CLI call.**
The window's **CLI 1회 조회** button is the only new execution path.

On a direct user click, the code checks all of:
- no running macro, scheduler, dungeon, fishing or multi-altering task
  (`AnyRunning`);
- no F9 setup (`_starting`) or existing production UI poll
  (`_productionPolling`);
- no existing gathering diagnostic or repeated common diagnostic query.

If busy, the UI displays a fixed reason and calls **no CLI method**.
When allowed, the existing `_starting` start guard prevents a
concurrent F9 from starting while the diagnostic is querying. The
one-shot read sequence uses only the five existing read-only CLI calls,
each once, through `ReadOnlyCliObservationBridge`:

1. `status` — explicit connected/disconnected result, but timeout != disconnection;
2. `get_activity` — validated activity and gathering classifications;
3. `get_altering_works` — server work-row counts and incomplete/ambiguous states;
4. `get_items` — validates item listing shape, **not** a material quantity change;
5. `get_alterable_items` — catalog row count only, **not** craftability/completion.

Queries follow existing CLI timeout policy; there is NO additional timer,
polling worker, automatic retry, artificial connection, packet capture or
hidden startup query. Closing the modeless diagnostic window cancels an
in-progress read; the next user click can start a fresh diagnostic.
F9/F10, macro inputs and the original recipe/receipt safety gates
remain owned by the original macro.

## What the window does and does not claim

`CommonObservationDiagnosticReport` maps only a fixed allowlist of
machine-generated status codes, typed states and validated item counts
to user-visible Korean descriptions. Unknown fields/status strings
render a generic `검증되지 않은 상태`. It never renders an arbitrary
CLI stdout field, item name, recipe name, character ID, token,
exception detail, device address or raw packet.

A read-only CLI work-row `Completed` is **not** item receipt. Empty
work list is **not** batch completion. A single inventory read is
**not** proof that rice, water or another missing material was gathered.
The window does not display character-specific plans or invoke
`execute_altering`, `execute_gathering`, `stop_action`, travel,
receipt or any input/retry API.

**Important implementation limit:** the diagnostic deliberately does not
compete with a running multi-altering macro. Without a separately
verified, current local production stage it displays **가공 단계 비교:
비교할 독립적 실행 단계가 없음**. The Phase 12 offline comparator can
detect specific inconsistencies using typed synthetic stages, but a
live-running error comparison is **not** implemented in this window and
must not be claimed. A future safe opt-in, no-extra-query shadow
observation path can be evaluated after measured read-only tests.

The common evidence engine remains disabled by default everywhere except
inside this explicitly clicked, temporary window interaction. It is
created just for that one check, and its in-memory records are cleared
afterwards. No state report or private source artifact is saved to disk
or sent to GitHub/AI. No decoding of Mobilay's live packet events exists.

## Automated validation

The `tests/common-observation` regression includes explicit-click-only
gate tests and redacted output tests. GitHub Actions run it alongside
packet/CLI and Mobilay-export diagnostics, the Windows macro build,
Abyss/Altering scheduler regressions, and the full production suite.
These are synthetic code/build tests, **not a manual UI click or a live
MabinogiMobile CLI validation on the user's PC**.

This is a draft-PR-only code change, NOT a production release,
installation, feature flag rollout, merge into `main`, or claimed
error-rate improvement.
