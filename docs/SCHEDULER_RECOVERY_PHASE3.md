# MabiAuto Scheduler Recovery — Phase 3 (safe, bounded read-only slice)

This update is limited to **scheduler resume preflight query recovery** and
**diagnostic-only checkpoint evidence**. It does not restart scheduler jobs,
automatically press game keys, replay crafting, register processing recipes,
receive items, modify ledger quantities, or bypass user resume confirmation.

## Bounded transient retry
Only the scheduler's read-only CLI capability and identity queries made inside
`ChooseSchedulerMultiAlteringAsync` use `SchedulerReadOnlyRetry`.
On IO/timeout or recognized temporary HTTP failures (408, 429, 500, 502, 503,
504 and network-without-status), retry **once** after 300 ms. All other
exceptions (including cancellation, permission/authentication errors, state
mismatches and failed durable-queue reconciliation) fail closed. An F10 cancel
immediately prevents another query.

The existing `MultiAlteringRecentResume.VerifyAsync` still checks persisted
batch identity, live work queues, receipts and inventory. It remains outside
the retry helper; if state cannot be proven the scheduler safely stops. No
automatic retry is enabled for any state-changing operation.

## Evidence-only boundaries
The existing local `scheduler-runtime.json` observation record now includes
`lastVerifiedBoundary`, `automaticGameActionRetry=false`, and
`resumeRequiresUserConfirmation=true`.
Two high-confidence observations can set the boundary:
- `altering_resume_user_verified`: a user explicitly selected resume after
  the existing live checks and confirmation dialogs.
- `crafting_input_released`: the crafting worker finished, released input
  ownership and no crafting error was reported.

**These labels are not recovery checkpoints granting permission to execute.**
They only tell a developer where the last verified evidence was collected.
A new run resets them to `none`; an old snapshot cannot start a new run.
F10 blocks new labels and the original cancellation semantics remain.

The actual durable multi-altering batch journal and manual resume proof stay
unchanged. Full end-to-end replay-safe automatic recovery would need new
transaction evidence before it could be enabled, especially for crafting.

## CI verification
Added deterministic tests to `tests/combined-scheduler/Program.cs` for
transient/no-retry failures, two-attempt budgets, F10 cancellation, and
evidence-only checkpoint semantics. The Windows build and production
regressions must pass. These tests do not prove live-game recovery.
