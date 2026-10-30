# Daily interruption regression

The three known blockers reset at 06:00 Korea time but can appear at any later
point. There is intentionally no day/hour gate and no "handled today" latch.
The production capture path checks on every capture; the run-scoped read-only
CLI checkpoint also polls during travel, gathering and facility waiting loops.
The handler is awaited in the SAME run, not a competing background input worker.

Run `dotnet run --project tests/daily-interruptions/Regression.csproj`.
On Windows this references the compiled application and also checks sanitized
800x1000 screenshot fixtures. The controller tests run portably with fake input.

Fixtures originate from the user's 2026-10-30 02:16 Stella Pick incident,
2026-10-25 07:15 NPC greeting, 07:16 field travel, 07:17 facility hub, and
IMG_1056.jpeg notice popup (client area normalized to 800x1000). Only recognition
chrome is retained in positive fixtures; advertisements, notices, character
names and currency observations are removed. These are detector fixtures,
not evidence of a successful live-game recovery.

The two positive frames authorize only ESC / the modal's own X, or recognized
NPC bubble / right-hand skip text. An unresolved modal remains bounded and
cancellable; unknown dialogs, ordinary facility menus, paid actions and task
registration/collection are never replayed by this handler. Real Korean WinRT
OCR and the exact live NPC skip label still need game-host validation.
