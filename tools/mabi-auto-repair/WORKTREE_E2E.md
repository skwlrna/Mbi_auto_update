# Real Git worktree + .NET repair-loop validation

The end-to-end tests create a temporary Git repository with intentionally incorrect C# arithmetic and a real .NET 8 regression executable.

1. The original C# regression MUST fail before modification (red).
2. A deterministic fake Codex operation edits only the isolated worktree, changing both C# source and the mode-specific C# regression.
3. The real dotnet runner MUST pass after the repair (green).
4. The original checkout stays clean and unchanged. The result remains a candidate, not a game-verified fix.
5. The successful Codex response with no code modifications MUST yield needs_review. Neither a repeat AI call nor a passing-candidate claim is allowed.

Windows and Linux GitHub Actions jobs run these tests with the real Git CLI, Python 3.11, and .NET 8 SDK. They do not run the actual Codex service, touch the live game or user PC, consume AI tokens, create GitHub PRs, or publish a release.

**Limitation:** A fake Codex patch demonstrates the actual Git and C# testing mechanics; it does not demonstrate an AI model fixing a real game bug. The most recent available runtime report was produced in V3.2.60 and has not been reproduced with V3.2.62, so paid repair is still appropriately blocked by the preflight gate.
