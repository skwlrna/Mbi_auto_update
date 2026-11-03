# Production no-progress advisory: phase 1 (offline-safe)

## Scope

This change adds **observability and operator notifications only** for
multi-altering long holds and crafting safe OCR deferral. It does not change
game input, retry timing, queue ordering, F10 cancellation, durable batch
ledgers, recipe selection, remote automation, Abyss gimmicks, or updater/release
behavior.

Warnings are **not** proof of a stall and are **not** recovery checkpoints.

## Multi-altering

`MultiAlteringCoordinator.RunAsync` observes *continuously unchanged*
per-facility states only:
- Persisted material-supply hold (reason key: exact plan identity).
- Typed safe **pre-input CLI read** quarantine.

After 15 minutes without either state clearing, emits **one** advisory for
that facility/reason. A different material plan, recovery, or completed facility
resets that facility's advisory clock; other facilities have independent clocks.
A new stock quantity does not reset the observation period if it remains the
same material-hold plan.

The message identifies facility, plan, material count/category or CLI read
quarantine, and elapsed minutes. This notification never calls registration,
collection, movement, crafting, retry or any game input. The existing
30-second read/stock recheck and durable F9 batch safety policies remain
unchanged.

## Crafting

`CraftingQueueCoordinator.RunAsync` measures elapsed time from the first
**verified zero-craft** OCR-only deferred recipe. It sends one advisory for
that queued recipe after 15 minutes of persistent safe OCR deferrals.
Confirming a recipe completion or a permitted material-skip clears its advisory
clock. Each recipe is tracked separately.

The 30-second retry cadence, existing round-robin recipe order and absence of
a fixed OCR-retry cap are unchanged. Unconfirmed clicks and partial batches
still stop rather than replaying them.

## Operator notification

The existing production dashboard displays a long-wait status and local
`AppLog` records a clear categorized message. The existing **configured**
Telegram notifier receives one text-only alert (no screenshot) per continuous
episode. These warnings do **not** invoke the error uploader or send a release,
and a notifier failure cannot alter the automation decision.

If a run is stopped or restarted, these in-memory diagnostic windows are reset;
no new durable auto-resume permission or persistent timer is introduced.

## Deterministic regression coverage

- `tests/altering/Program.cs`: 14-minute silence, 15-minute one-shot alert,
  independent facilities, material versus safe CLI-read cause, reason change,
  cleared hold.
- `tests/crafting/Program.cs`: per-recipe 15-minute one-shot behavior; virtual
  clock running the **real** `CraftingQueueCoordinator` confirms its original
  30-second delay request and success order despite repeated OCR deferral.

Both run in the existing Windows production CI. No real game is launched and
no production release is authorized.

## Known limitations

The advisory clock follows the observed state: it cannot distinguish a genuine
game stall from a legitimate long wait, prove an unknown screen transition,
or independently confirm post-click actions. Automatic restart or retry
escalation would require separate review and evidence.
