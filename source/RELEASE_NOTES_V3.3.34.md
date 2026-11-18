# Mabi Auto V3.3.34

- Fix the 2026-11-18 16:12 KST F9 leather+ tannin alert: inventory 1,275 / one-ticket requirement 4 must not hide another real missing leather/hide input.
- When fresh get_items stock is sufficient but recipe Owned disagrees, inspect other missing inputs first, then request one additional fresh same-identity recipe snapshot.
- Only a verified fresh Alterable=true recipe may clear the stale-inventory HOLD. The unchanged per-slot, scene, queue, input, ledger and receipt checks still apply.
- If the live recipe continues Alterable=false, log its exact Owned / Reason and stay in the isolated no-input hold. Never force a click based on bag count alone.
- Keep 7-slot leather batching, recursive leather intermediates, external raw/tannin non-autogather policy, and unrelated Abyss/scheduler behavior unchanged.
- Run Windows main CI, actual-stock regression and publisher smoke test before public release. Real-game acceptance pending. Rollback via ROLLBACK_PREVIOUS.cmd.
