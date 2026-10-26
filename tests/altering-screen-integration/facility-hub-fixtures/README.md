# 2026-10-26 16:13:26 KST multi-altering facility hub incident

`hub-wood-20261026-161326.png` is the **unaltered** game screenshot
uploaded to `skwlrna/Mbi_auto_log` under:

`runtime-errors/2026-10-26/20261026_161326_135_다중가공_오류_5dc7b7/screen.png`

Source and test Git blob SHA: `d5edc5df89ef6e7b90f5eadcc31d09fc939f1b42`.

Recorded version: V3.2.64. The macro was recovering from a metal
facility to collect 7 completed wood-processing jobs. Its first hub-card
click was at (400,261); a later K returned the game to the six-card hub,
but the 3-pass facility-entry loop terminated before another verified
card selection. The screenshot shows the hub, **not** a successful wood
facility entry.

Integration tests assert that this capture passes the real compiled hub
detector and is not a single-facility screen. Separate policy tests
cover final-pass authorization and distinct body click coordinates for
all six known processing cards.

Offline tests do not send clicks, enter a live game, perform receipts,
or prove that the second click succeeds in a running client.
