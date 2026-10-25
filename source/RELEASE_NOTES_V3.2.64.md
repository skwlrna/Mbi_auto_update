# Mabi Auto V3.2.64 — Remove automatic repair system

This update retires the dedicated AI automatic repair system at the user's request.

- Remove the GitHub automated repair scanner, PR promoter, release promotion workflows and standalone Python repair tool.
- After the new application passes startup health checks, run a bundled one-time-scope cleanup of the named Windows scheduled task `MabiAutoRepair-Watch` and the dedicated user profile folders `.MabiAutoRepair` and `MabiAutoAI`.
- The cleanup runs from the updated application without manually entering PowerShell commands. Its result is written under LocalAppData/MabiAuto/auto_repair_retirement.log.
- Preserve the normal macro, runtime error logging, version update checks and last-version rollback mechanism.
- Exact CI-verified Windows Lite binary plus SHA256 are required before a public non-prerelease is published.

Real-game behavior and a particular user's successful local cleanup are not established by offline CI. Existing `Mbi_auto_log` records and ordinary update rollback support are intentionally retained.
