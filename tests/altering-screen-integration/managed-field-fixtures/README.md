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

The corpus additionally includes the owner's unaltered V3.2.76 screenshots:
- `field-workshop-20261031-022454.png`: blob `50848429f80d96e010d1eed6603a31e844c96f6c`.
- `field-workshop-20261031-070147.png`: blob `972afb624b08ac5d25851c797b9de24c0e38248f`.

Tested boundaries:
- No minimap colour, rim, shape or day/night marker participates in field evidence.
- Upper shortcut glyphs (Home/ESC, plus End when available) and BOTH lower K/I glyphs are required.
- Every historical and new field remains positive with the whole minimap blacked out.
- Removing K, I or the upper shortcut labels rejects field evidence.
- Empty frames, wrong viewports, production hubs and actual travel popups remain blocked.
- Two actual captures, latest input-frame verification and fresh CLI idle remain required.
- Offline frames verify recognition only, not live movement or collection success.
