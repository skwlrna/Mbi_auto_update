# N03: cancellation at the native input boundary

Baseline: `main` / `4cc6026fdcb345d999dfafdb5b7537907af240f3`.
Scope: N03 only. No release, version, coordinates, OCR conditions, scheduler,
facility Fresh/Reuse policy, recipe decisions, CLI or wing guards are changed.

## Cause and implementation

Previously `GuardedInputController.Verify` checked a run token, but
`InterceptionInput` did not receive it. Focus/cursor sleeps could therefore be
followed by a new DOWN after F10. Paste also lacked exception-safe Ctrl/V release.

- `IInputController` requires a token at the low-level boundary for click, drag,
  scan-code tap and paste. No token-free Interception overload remains.
- `ProductionUiRuntime` and raw `PeacaRouteEngine` calls pass their action token.
  Legacy `ScenarioEngine` calls use the token bound to `GuardedInputController`;
  unbound calls fail closed. Explicit tokens are linked with the bound token.
- A cancelled guarded runtime cannot be re-armed with a fresh cleanup token.
  This is intentional N03 behavior: read-only CLI/wing verification can continue
  under its existing bounded verification tokens, but it cannot start new DOWNs.
  In gathering, a cleanup Stop action after cancellation cannot re-arm that same
  runtime. Normal uncancelled stop actions retain their existing behavior.
- `CancellableInputSequence` owns the existing serialized click/drag/key/paste
  sequences and unchanged normal delay values. `InputLock` checks cancellation
  before and after lock acquisition, including every 20 ms while contended.
- Every move or DOWN uses `InputSendPermit`. Window activation, movement,
  movement retry, all waits and the final DOWN boundary check cancellation.
- DOWN attempts are tracked before calling transport, including a transport that
  throws after accepting a DOWN. Matching UP runs in `finally` without a cancelled
  token. Paste releases Ctrl even if V release throws. A failed UP stops future
  operations on that input instance; DOWNs are never automatically retried.
- The cancellation callback only performs `Interlocked.Exchange`. It never
  takes the input/window lock or waits for a native send. Lock order remains
  shared guard gate -> instance input gate; neither callback reverses that order.
- The clipboard STA worker cannot transmit input. It is explicitly a
  **background** thread, so a stuck Windows clipboard call cannot keep the app
  process alive after shutdown. Its caller exits promptly on cancellation.
- Clipboard writes are serialized across runs using a shared worker gate. A
  cancelled worker still waiting for the gate checks its token before writing;
  an already-running cancelled writer finishes (or remains blocked) before a
  restarted run can commit a newer clipboard value. Thus an old STA worker
  cannot overwrite a newer successfully prepared search term. A native
  Clipboard.SetText already in progress cannot be forcibly terminated; if it
  hangs, later clipboard operations wait until cancelled rather than bypassing
  the gate and accepting a stale write.

## Precisely bounded guarantee

The atomic exchange to permit state `cancelled` invalidates future admission.
CAS `open -> admitted` is the linearization point for one send. Token checks both
before CAS and immediately after CAS also catch cancellation whose callback is
not yet dispatched. No delay, focus work or cursor verification follows admission
before the transport call. After one admitted send finishes, cancellation keeps
future admission closed.

If cancellation wins before admission, a new DOWN is not sent. If admission wins
and cancellation arrives in the last managed/native boundary instructions or
inside `interception_send`, that already admitted call cannot be recalled.
`CancellationToken` cannot atomically transact with a kernel driver; this change
must not be described as zero possible driver events after the physical F10 key.
F10 must first be delivered through Windows hotkey dispatch and cancel the run.

UP is guaranteed to be **attempted** for every attempted DOWN, even after
cancellation or a DOWN exception. Successful physical release is conditional on
the native driver accepting UP. A broken/hung driver, process termination, or OS
failure cannot be corrected by a managed `finally`. UP failure is surfaced and
new input is blocked; failed native input is not blindly retried.

## Deterministic regressions

Run on Windows (executes the compiled production app via ProjectReference):

```powershell
dotnet run --project tests/n03-input/Regression.csproj -c Release -r win-x64
```

The injected `InterceptionInput` constructor never creates a driver context. The
fake window, fake transport and controlled timing are the only external effects.
Linux uses linked copies of the same production sequencing/guard source for a
portable pass; Windows CI uses the real compiled `InterceptionInput` wrapper.

The suite reports each named case. Covered families:

- Click cancellation before call; while actually contending for either lock;
  after successful upper Verify; after activation; before/after move; during
  cursor retry wait; after retry verification immediately before DOWN; after
  DOWN; before UP. Exact DOWN/UP counts and absence of subsequent clicks checked.
- Actual cancellable timer wake-up, concurrent cancellation while a driver call
  is deliberately blocked, permit invalidation before admission. Bounded waits
  fail on deadlock. Lock contention is signalled only after TryEnter fails.
- Space (`39`), K (`25`), Esc (`01`) cancellation before call, after upper Verify
  and after DOWN. Consecutive keys stop without a second independent input.
- Paste cancellation at clipboard preparation, Ctrl DOWN, V DOWN and V UP;
  Ctrl/V order and released state. Drag cancellation after DOWN and mid-movement.
- Windows-only compiled-production STA tests check cancellation before write,
  cancellation during blocked STA write (prompt F10 return, background STA),
  restart write ordering, and cancellation of queued stale writers.
- DOWN/wait exceptions, attempted release, Ctrl release despite V UP failure,
  and failure blocking any future input on that instance.
- Normal click/Space/paste/drag, unchanged target coordinates, click cursor
  tolerance and exactly one retry, all existing sequence delays.
- Guard coordinate bounds, capture/window 800x1000 size, moved origin, foreground
  checks and original eight focus attempts (80 then seven 120 ms waits), minimized
  window, invalidated/stale frame, wrong window, unbound token, normal focus retry,
  and blocked fresh-token re-arm after cancellation.

A local negative-control experiment temporarily replaced only the guard's inner
click token with `CancellationToken.None` (the original missing-token failure
mode). `guard/cancel-after-verify-before-low-level-entry` failed with
`Expected cancellation`; restoring propagation made the suite pass. This is a
controlled missing-token reproduction, not a claim that a game repro occurred.

## Validation classification

- **Confirmed:** the N03 code path and missing token propagation at the baseline;
  all IInputController implementations/callers reviewed; F10 in MainForm already
  cancels the applicable run sources. MainForm and ZeroWingScreenGuards require
  no edits to propagate the token and remain unchanged.
- **Passed (portable local):** N03 deterministic regressions; existing altering
  238 checks, crafting 75 checks and gathering 64 checks. Local .NET CLI could
  not start because Process.StartTime is unsupported in this host; these local
  passes were compiled with .NET 8 Roslyn and reference assemblies and executed
  with the .NET 8 runtime. They do not stand in for an MSBuild/Windows build.
- **Windows validation:** CI adds the N03 executable step; all existing Windows
  build, static guard locks, altering-screen-integration, altering, crafting,
  gathering, production UI and dungeon retry checks remain intact. Consult the
  PR's Actions run for their final results.
- **Conditional:** in-flight native call boundary and physical UP acceptance as
  described above. No newly introduced cancellation callback blocks on a driver.
- **Not Tested:** real game, installed Interception driver, physical F10 latency,
  native/clipboard hangs on a user's Windows desktop, and real gameplay in single
  altering, multi altering, crafting, gathering and dungeon modes. The separate
  fishing IInputSender subsystem does not implement this interface and is outside
  N03; it is unchanged and no broader safety claim is made for it.
