# Real field HUD capture regression: cave and historical cases

All images are unmodified, 800x1000 screenshots copied by Git blob SHA
from `skwlrna/Mbi_auto_log`:

| Fixture | KST time | Version | Blob SHA |
|---|---|---|---|
| `cave-20261026-174515.png` | 2026-10-26 17:45:15 | V3.2.66 | `6c2d6612ff9728b68d69e19bf83b61c35373baab` |
| `workshop-20261023-060800.png` | 2026-10-23 06:08:00 | V3.2.55 | `02805977046c8147bb7c9f747ce62a2a333ff476` |
| `gold-world-20261020-234501.png` | 2026-10-20 23:45:01 | V3.2.48 | `6eeed03e6542a626590186ed342ba93b05bae4c6` |

The October 26 cave image has strong K/I templates and stable shortcut
glyph scores but *no gold minimap*; the older pixel-only world proof was
false. The October 23 workshop screenshot also has a blue/cyan minimap
border, but its K template score was weaker, so it is included as a
historical **visual** regression, not evidence of fully successful
authorization. The October 20 screenshot has a gold-rim minimap and
strong Home/End but weak K/I, so the existing gold route must remain
valid without depending on the new blue route.

New blue fallback requires the separately detected blue-rim circle,
teal K disc, full accurately located Home+End+ESC glyphs on two frames,
no blocking modal/processing menu, fresh CLI idle, and a last-moment
input-frame verification. A blue-looking background alone cannot be
permission for K. Existing routes and 2-second time limit are unchanged.

These offline screenshots cannot prove whether a subsequent real-game
K press and receipt succeeds.
