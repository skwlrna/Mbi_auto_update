# Managed F9 K world-HUD incident captures (read-only fixtures)

Original *unaltered* 800x1000 runtime-error screenshots from
[skwlrna/Mbi_auto_log](https://github.com/skwlrna/Mbi_auto_log).

| Original incident time (Asia/Seoul) | Recorded app | File | Git blob SHA |
|---|---|---|---|
| 2026-10-20 23:45:01 | V3.2.48 | `city-20261020-234501.png` | `6eeed03e6542a626590186ed342ba93b05bae4c6` |
| 2026-10-23 06:08:00 | V3.2.55 | `workshop-20261023-060800.png` | `02805977046c8147bb7c9f747ce62a2a333ff476` |
| 2026-10-26 17:45:15 | V3.2.66 | `cave-blue-20261026-174515.png` | `6c2d6612ff9728b68d69e19bf83b61c35373baab` |

**Important distinction:** In the actual 17:45 `recent.log`, nine consecutive
pre-input HUD checks over ~2.1 seconds saw K/I ~0.993/0.902 and the top three
shortcut glyphs ~0.958/0.960/1.000, but `필드독립증거=False`. The *saved
post-stop screenshot* on Windows CI nevertheless passes both the previous
gold/teal gate and the new blue/teal gate. This does **not** establish that
blue versus gold coloring alone caused the failure. It documents a real
transient proof mismatch between earlier frames and the incident capture.

A guarded extra route now requires two consecutive blue-rim/teal-K field
observations, K+I image templates, and stable Home/End/ESC location, followed
by an independent final-frame verification and fresh idle CLI check.
Existing modal/menu vetoes, timeouts, and no-input-on-uncertainty remain.

Tests run entirely offline against the real compiled application's
pixel and policy predicates. They neither send game input nor demonstrate
that future live cave frames will authorize K.

The prior 10/20 and 10/23 saved screens also satisfy both static color
detectors in CI, so their original *runtime* K-input failures cannot be
attributed only to lacking visual detector support on the saved image.
