# Mabi Auto V3.2.76 — daily interruption recovery

User-requested public auto-update release of merged PR #342, commit fe695aca9a23f6c1360692ac0de1c51ec5508ce0, plus release-only version metadata.

- Handle daily Stella pick overlay, NPC greeting dialogue, and varying notice popup content while preserving the active automation workflow.
- Recognize daily interruptions at any time, including after the 06:00 KST reset, and resume only after visual confirmation.
- Fail closed when screen classification is ambiguous; do not blindly repeat ESC, X, or dialogue-skip inputs.
- No separate manual popup action is required when visual recognition succeeds.

## Validation and safety
- PR #342 CI: daily interruption recovery, managed K HUD, Abyss + Altering Scheduler, V3 Production Engine, material OCR passed.
- Merged main production CI for the exact new version is mandatory before release, along with N02, F05, updater rollback, Windows binary smoke, source ZIP, SHA256, and BUILD_COMMIT checks.
- **Real-game testing has not been performed or verified**. Test after installation; use bundled ROLLBACK_PREVIOUS.cmd if needed. Previous V3.2.75 assets are not overwritten.
