# Codex quota exhaustion → ChatGPT manual handoff (MabiAuto)

This feature reduces unexpected Codex usage during early scheduler rollout.
It **does not connect a Windows watcher directly to a ChatGPT conversation**.
No ChatGPT account/plan visibility, OpenAI API key, automatic cloud upload,
automated game recovery or auto-deployment is added.

## Existing limits preserved

- Default maximum: 8 AI error investigations per local day.
- Newest stable MCP reports handled first.
- Exact duplicate error evidence within 10 minutes is skipped. Newly
  recorded signatures now survive watcher restarts, so restarting the app
  does not immediately re-spend Codex usage on the exact same evidence.
- The existing bounded Codex retry for transient 429/503 transport errors
  remains; ordinary 429 is **not** interpreted as exhausted credits.
- Codex remains sandboxed in isolated worktrees; no live game input,
  unreviewed source merge or deployment is authorized.
- The legacy Autofix-OFF gate, F10 and child-watcher termination on normal
  application close are unchanged.

## Codex account quota exhausted

A clear provider quota/credit exhaustion response (not an arbitrary error)
sets `quota_pause` in the existing local
`~/MabiAutoAI/autopilot-results/watch-state.json`. While paused:

1. **Zero further Codex calls** are made by this watcher, even across
   watcher restarts or new incoming scheduler errors.
2. The original error evidence and the blocked result stay on the PC.
   The quota-blocked attempt is not automatically replayed; its isolated
   worktree is retained for review.
3. New MCP errors remain unprocessed on disk with stable event IDs.
4. `watch-activity.json` reports `quota_paused` and queue length.
5. `chatgpt-handoff.json` provides a **bounded local-only** summary of
   the 20 newest queued/held errors: hashed event IDs and heuristic
   category enum values. It does not copy raw logs, screenshots, secrets,
   local source paths or MCP report content. The actual total is recorded
   independently.

The `AI 현황 → ChatGPT 인계 복사` button **only copies** this summary to
the user's Windows clipboard, after a strict shape/enum validation. The
user pastes it into an existing ChatGPT conversation. ChatGPT can inspect
GitHub and request additional user-reviewed redacted evidence when needed.
No background ChatGPT task is created; no extra API subscription or API
billing is silently activated.

**Daily MabiAuto 8-run cap** also creates a local handoff summary for
unprocessed reports. This cap is distinct from the Codex provider quota.

## Codex restart after account reset

The `AI 현황 → Codex 한도 재개` button is enabled through a manual
confirmation asking the user to verify their account's usage limit has
reset. It creates `quota-resume-request.json` locally containing a
one-time request tied to the current random pause ID. The running watcher
consumes it on its normal polling cycle. Stale or mismatched pause IDs do
not unblock processing. The paused result itself is **not replayed**;
remaining unprocessed errors are processed subject to existing limits.

## Validation

Offline unit tests in `tests/ai-autopilot/test_quota_handoff.py` cover
quota phrase detection vs generic HTTP 429, immediate retry suppression,
persistent pause, new error backlog, strict user-confirmed resume, privacy,
same-error dedup across watcher restarts and the daily cap. Windows CI
will run these with the existing Python and C# regression suites.

**Limitations:** detecting quota from a CLI message is best effort:
undocumented provider wordings may not be recognized, and generic
network/rate-limit errors have separate handling. A green offline test
does not validate the MabiAuto watcher on the user's own PC.
