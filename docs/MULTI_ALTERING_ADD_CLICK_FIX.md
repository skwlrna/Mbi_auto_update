# Multi-altering '+ 추가' button feedback fix

Reported after V3.2.11 live smoke test: the multi-altering **+ 추가**
button appears to do nothing.

## Cause from source inspection (not yet proven on user's PC)

`MainForm.ProductionPage.cs` previously set `_addQueue.Enabled`
using the same `available` boolean as the **F9 execution guard**.
If an altering recipe was marked `Alterable=false` and supplied no
concrete missing-ingredient list, the button was greyed out and could not
receive a click. When a selected recipe failed plan validation inside
`AddCurrentAlteringQueue`, the exception was recorded only in the
ordinary log and no in-page explanation appeared.

Older runtime logs in `skwlrna/Mbi_auto_log` concern a different
post-collection game-side error from V3.2.4; those logs do not prove
the cause of the V3.2.11 configuration button report.

## Fix scope

- **Only while idle**, the + 추가 control remains clickable even if a
  selected recipe is unavailable or absent. The click handler always
  reports a visible reason (selection missing, catalog loading, failed
  plan verification) or successful add/target update.
- Preserve `SelectedAlteringPlan` checks: ambiguous/unverified
  recipes are **not added to the queue** or started. The existing F9
  `available` execution rule is unchanged. F10/game inputs, batch
  journal, resume and scheduler ordering are unchanged.
- Add an always visible one-line status to the multi-altering queue
  area; it stays readable during scale changes.
- The user can now distinguish a UI input problem from blocked
  recipe/capability conditions without guessing.

## Offline regression

Windows `tests/production-ui/Program.cs` now checks:
- With no recipe selected, + 추가 responds and gives a visible reason.
- With an unverified recipe, + 추가 responds **but does not add any
  item or send CLI commands**, and F9 remains blocked.
- With an eligible recipe, + 추가 queues it and updates the list.
- The new status label and add target remain inside the screen
  layout for the existing supported window sizes.

Actual user-side reproduction needs a screenshot and/or a new
V3.2.11+ diagnostic trace to confirm the observed condition.
