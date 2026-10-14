# MabiAuto V3.2.3 — Integrated AI Error Management

The **Log / Diagnostics** screen now has an **AI Error Management** panel. It
controls the *existing* Python Autopilot companion directly; no separate CMD
window is required. Run-time changes are limited to a dedicated Git worktree.

## Getting started (once per PC)

1. Keep the existing MCP error reporter configured and running. Install Python
   3.10+, Git, .NET 8 SDK, and Codex CLI, and sign into Codex.
2. The local source clone must exist at `%USERPROFILE%\MabiAutoAI\repo` and
   the legacy `tools\codex-autofix\autofix.py --status` must say **OFF**.
3. Open **Log / Diagnostics** in MabiAuto and click **AI Start**.
4. Enable **Start automatically next time** to start it whenever MabiAuto runs.
   The preference is stored under `%LOCALAPPDATA%\MabiAuto\ai-autopilot.json`.
5. The panel displays setup and safe-stop status. Details are saved under
   `%USERPROFILE%\MabiAutoAI\autopilot-results`.

## Safety boundaries

- **Default is OFF**; users must opt in to enabling and persisting auto-start.
- Each watcher holds an operating-system lock (`autopilot.lock`). If a separate
  v1.3 watcher is already running, the integrated child exits without starting
  a duplicate watcher. **Stop standalone v1.3 before using the integrated UI.**
- Source edits are in isolated Git worktrees. There is NO automatic merging,
  GitHub push, binary replacement, game input, or deployment.
- Existing unverified AI patches fail closed. A new installation without prior
  AI patches can watch *new* reports while leaving deployment disabled.
- Codex may receive up to bounded, redacted excerpts of MCP error logs. Do not
  enable the AI watcher if logs may contain personal or sensitive information.
- Closing MabiAuto stops the child watcher that MabiAuto launched. Any active
  isolated repair may be interrupted; it is **not** applied to production.
- This program does **not** install Python, Codex, .NET SDK, MCP, or a Windows
  service. It controls compatible dependencies already installed on the PC.
- `tests_passed_review_required` means offline tests passed, not that an error
  is fixed in the live game. Human and live-game validation remain necessary.

## Release verification

V3.2.3 must pass Windows GitHub Actions compilation and regression tests before
its release workflow creates binary assets. This integration does not change
release eligibility safeguards or automatic updater trust checks.