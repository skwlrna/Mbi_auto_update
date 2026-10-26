# Historical F9 managed K world-HUD incident corpus

All five PNGs are exact, unaltered `screen.png` files from the owner's `skwlrna/Mbi_auto_log` repository. They are used in offline compiled production L2 regression tests, never to perform a live keypress.

| Captured at (KST) | Version | Incident | Git blob |
| --- | --- | --- | --- |
| 2026-10-13 14:16:48 | V3.1.99 | `20261013_141648_988_다중가공_오류_b7dc34` | `f12e71e4738198a3be778e83287e3bce288c82ae` |
| 2026-10-20 23:45:01 | V3.2.48 | `20261020_234501_149_다중가공_오류_04b71d` | `6eeed03e6542a626590186ed342ba93b05bae4c6` |
| 2026-10-23 06:08:00 | V3.2.55 | `20261023_060800_480_다중가공_오류_3afdff` | `02805977046c8147bb7c9f747ce62a2a333ff476` |
| 2026-10-26 17:45:15 | V3.2.66 | `20261026_174515_690_다중가공_오류_0594dd` | `6c2d6612ff9728b68d69e19bf83b61c35373baab` |
| 2026-10-26 19:09:33 | V3.2.67 | `20261026_190933_946_다중가공_오류_8e33a5` | `2d045b7a00428017c3e4991b85a382274ef4a9e0` |

All originals reside under `runtime-errors/YYYY-MM-DD/<incident>/screen.png`. The latest cave incident logged K/I template scores around 0.993/0.902, actual Home/End/ESC shortcut glyphs ~0.958/0.960/1.000, but a false independent gold-minimap field score. Historic incidents logged alternative combinations of 2/4 stable HUD icons.

Tested boundaries:
- Original gold-rim + teal K path remains unchanged for historical outdoor screenshots.
- New non-gold path requires the K skill disc, green Space compass ring and blue circular minimap border. The orange day/night clock icon is explicitly **not required**. Real 17:45 and 19:09 error screenshots both must pass the field-proof predicate. The independent K/Space/minimap regions are selectively removed to verify rejection.
- Menu, popup, unknown frame and stale HUD remain blocked via existing two-frame authorization, last-input frame, fresh CLI idle and modal veto. The new pixel predicate alone never authorizes a K input.
- These are offline frames, not proof of successful input or completed in-game processing.
