# F9 processing latency review — verified transitions, not blind waits

Branch: `work/f9-unified-scene-arbiter-20261028` · draft PR #341 · offline only.

## Changes made

1. **Processing K-menu tab → hub:** managed F9 no longer sleeps an unconditional 550 ms after the final-frame-verified click. `EnterFacilityCoreAsync` immediately polls for a positive processing-hub scene for up to **1,450 ms**, retaining the former combined `550+900` ms transition window. Existing fresh CLI-idle, modal veto, independent frames before click, verified final click and positive target-hub check remain. The **Automatic** (single-altering) path keeps the original behavior.
2. **Processing hub facility card → exact facility:** managed F9 starts the positive exact-title/facility visual polling immediately after the final-frame-verified card click. Requires **two positive consecutive facility captures** plus no modal and exact title. The managed bound becomes **3,550 ms** (the previous 550 ms sleep + 3,000 ms polling). The Automatic path retains 550 ms + 3,000 ms unchanged.
3. **Compiled regression:** `tests/altering-screen-integration/Program.cs` asserts the managed tab's compiled code still has the single 160 ms independent-frame delay but **no blind after-click 550 ms delay**, and that facility/hub transition polling and final-frame click guards remain in production code.

## Time comparison (upper bounds vs typical)

| Transition | Previous | Now | Interpretation |
|---|---:|---:|---|
| Managed F9 processing tab to hub | 550 ms blind + up to 900 ms positive poll | Up to 1,450 ms positive poll, starting immediately | Potential roughly 550 ms saved if hub appears early; **maximum allowed transition remains equal** |
| Managed F9 hub card to named facility | 550 ms blind + up to 3,000 ms exact two-frame poll | Up to 3,550 ms exact two-frame poll, starting immediately | Potential roughly 550 ms saved if facility appears early; **maximum allowed transition remains equal** |
| Registration to optional material transfer detection | six 200 ms visual polls, 1,200 ms if absent | **Unchanged** | Delayed popup safety. High potential per-work latency but need actual response-time telemetry and modal lateness before shortening |
| Registration CLI confirmation | 250 ms for first 8 observations, then 1 s fallback | **Unchanged** | Fast confirmation already used; shortening is unproven |
| Lane completion wait | Server-reported remaining seconds, clamped 1–30 s | **Unchanged** | Actual work duration/CLI scheduling, not gratuitous UI idle |
| Receive Space/result | 450 ms settle and results/CLI/retry guards | **Unchanged** | No speculative speed-up across irreversible receipt |
| K/onsite scene stability | two distinct frames; travel/CLI/modals | **Unchanged** | Safety-critical, not a source of blind waits |

The reductions are **opportunities, not guaranteed wall-clock savings**. Exact realized speed needs live instrumentation across registration cycles; async screenshot capture/OCR, CLI response and game timing dominate when slow.

## No weakened safety invariants

- F10 stops subsequent inputs; avoid clicks over modal, unknown scene, other-module exclusive UI ownership or CLI travel.
- A fixed click requires final-frame verification; selection after a click still requires exact facility identity and two independent stable frames.
- 7-slot ownership, registration +1 CLI evidence, journal persistence and final receipt Space cannot be skipped.
- Do not change the separate `Automatic` single-altering path.
- **No automatic release/merge/updater**. CI is an offline acceptance step; game observations occur only after a separately approved deployment.

## Future measurement after user-authorized update

Record non-sensitive interval timestamps for each named transition, receipt and modal appearance over ≥30 registrations with popup absent and popup present, and compare p50/p95/maximum latency and safety-stop counts. If late material-transfer modal arrivals are proven bounded and no false-positives, only then consider reducing the registration post-click modal polling window. Preserve modal/CLI evidence and no-duplicate guarantees.
