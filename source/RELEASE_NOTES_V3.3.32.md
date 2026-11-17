# Mabi Auto V3.3.32

- Fix multi-altering crash when several parent facilities await one shared intermediate production batch.
- After confirmed child receipt and intermediate lease release, clear all parent wait entries for that same child; each parent is revalidated on the next CLI manager turn.
- Preserve existing safety stops, durable ledger, inventory, recipe, and pre-input verification.
- CI validation required before publication. Real-game acceptance pending.
- Roll back using the bundled ROLLBACK_PREVIOUS.cmd if needed.
