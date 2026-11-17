# Mabi Auto V3.3.33

- Fix 2026-11-18 00:04:32 KST F9 multi-altering gathering stop dead-end after rice stock reached 604/600.
- Count only actually transmitted Stop Space inputs; a deferred first probe does not consume the retry budget.
- Allow at most two actual Space transmissions, each guarded by fresh CLI Stop and visible Stop UI. Enforce verified persistent Stop observations and cooldown before subsequent transmissions.
- Preserve no-input fail-closed behavior for CLI/UI disagreement, existing receipt/registration journal, Abyss, scheduling, material selection and rollback.
- Include deterministic gathering stop regression and retain V3.3.32 checks.
- Windows CI and release gate required. Actual in-game acceptance pending; use ROLLBACK_PREVIOUS.cmd if new behavior is incorrect.
