using System.Drawing;
using DungeonVisionBot;

static class Program
{
    static int passed;
    static readonly Point Target = new(340, 724);
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Test(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    static void Cancelled(Action action)
    {
        try { action(); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
    }
    static void Failed(Action action)
    {
        try { action(); throw new Exception("Expected input failure"); }
        catch (InvalidOperationException) { }
    }
    static void Complete(Task task)
    {
        Check(task.Wait(TimeSpan.FromSeconds(3)), "Deadlock or delayed cancellation");
        task.GetAwaiter().GetResult();
    }
    static void MouseBalanced(Fake f, int downs)
    {
        Check(f.Events.Count(x => x == "MD") == downs, "Unexpected MouseDown");
        Check(f.Events.Count(x => x == "MU") == downs, "Missing/duplicate MouseUp");
        Check(!f.MouseHeld && f.KeysHeld.Count == 0, "Stuck input");
    }
    static void Main()
    {
        Test("click/cancel-before-call", () =>
        {
            using var f = new Fixture(); f.Cts.Cancel(); Cancelled(f.Click); MouseBalanced(f.Fake, 0);
        });
        foreach (var checkpoint in new[] { "activated", "wait:100", "moved:1", "wait:70", "cursor:1" })
        {
            Test("click/cancel-" + checkpoint, () =>
            {
                using var f = new Fixture(); f.Fake.Hook = p => { if (p == checkpoint) f.Cts.Cancel(); };
                Cancelled(f.Click); MouseBalanced(f.Fake, 0);
            });
        }
        Test("click/cancel-retry-wait", () =>
        {
            using var f = new Fixture(); f.Fake.CursorFailures = 1;
            int waits = 0;
            f.Fake.Hook = p => { if (p == "wait:100" && ++waits == 2) f.Cts.Cancel(); };
            Cancelled(f.Click); MouseBalanced(f.Fake, 0); Check(f.Fake.Moves == 2, "Retry count changed");
        });
        Test("click/cancel-after-retry-cursor-before-down", () =>
        {
            using var f = new Fixture(); f.Fake.CursorFailures = 1;
            f.Fake.Hook = p => { if (p == "cursor:2") f.Cts.Cancel(); };
            Cancelled(f.Click); MouseBalanced(f.Fake, 0);
        });
        foreach (var checkpoint in new[] { "MD", "wait:75" })
        {
            Test("click/cancel-" + checkpoint, () =>
            {
                using var f = new Fixture(); f.Fake.Hook = p => { if (p == checkpoint) f.Cts.Cancel(); };
                Cancelled(f.Click); MouseBalanced(f.Fake, 1); Cancelled(f.Click); MouseBalanced(f.Fake, 1);
            });
        }
        Test("click/cancel-during-real-timed-wait", () =>
        {
            using var f = new Fixture(realTiming: true);
            using var moved = new ManualResetEventSlim();
            f.Fake.Hook = p => { if (p == "moved:1") moved.Set(); };
            var click = Task.Run(() => Cancelled(f.Click));
            Check(moved.Wait(TimeSpan.FromSeconds(3)), "Move not reached");
            f.Cts.Cancel(); Complete(click); MouseBalanced(f.Fake, 0);
        });
        Test("click/cancel-while-waiting-input-lock", () => LockWait(false));
        Test("guard/cancel-while-waiting-shared-lock", () => LockWait(true));
        Test("guard/cancel-after-verify-before-low-level-entry", () =>
        {
            using var f = new Fixture();
            using var forwarding = new ForwardingInput(f.Input, f.Cts.Cancel);
            using var guard = new GuardedInputController(forwarding, f.Fake, f.Fake);
            guard.SetCancellation(f.Cts.Token); guard.ObserveFrame(1, new Size(800, 1000));
            Cancelled(() => guard.ClickClientPoint(1, Target)); MouseBalanced(f.Fake, 0);
        });
        Test("permit/cancel-before-admission", () =>
        {
            using var cts = new CancellationTokenSource(); using var permit = new InputSendPermit(cts.Token);
            cts.Cancel(); int sends = 0; Cancelled(() => permit.Send(() => sends++)); Check(sends == 0, "Admitted after invalidation");
        });
        Test("permit/cancel-does-not-wait-for-in-flight-driver", () =>
        {
            using var f = new Fixture(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            f.Fake.Hook = p => { if (p == "MD") { entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(3)), "Blocked driver timeout"); } };
            var click = Task.Run(() => Cancelled(f.Click)); Check(entered.Wait(TimeSpan.FromSeconds(3)), "Down not reached");
            try { Complete(Task.Run(f.Cts.Cancel)); } finally { release.Set(); }
            Complete(click); MouseBalanced(f.Fake, 1);
        });
        foreach (ushort key in new ushort[] { 0x39, 0x25, 0x01 })
        {
            Test($"key/{key:X2}/cancel-before", () =>
            {
                using var f = new Fixture(); f.Cts.Cancel(); Cancelled(() => f.Tap(key)); Check(f.Fake.Events.Count == 0, "Key sent after cancellation");
            });
            Test($"key/{key:X2}/cancel-after-verify-before-low-level", () =>
            {
                using var f = new Fixture();
                using var forwarding = new ForwardingInput(f.Input, f.Cts.Cancel);
                using var guard = new GuardedInputController(forwarding, f.Fake, f.Fake);
                guard.SetCancellation(f.Cts.Token); guard.ObserveFrame(1, new Size(800, 1000));
                Cancelled(() => guard.TapScanCode(key)); Check(f.Fake.Events.Count == 0, "Key bypassed low-level cancellation");
            });
            Test($"key/{key:X2}/cancel-after-down", () =>
            {
                using var f = new Fixture(); f.Fake.Hook = p => { if (p == $"KD:{key:X2}") f.Cts.Cancel(); };
                Cancelled(() => f.Tap(key)); Check(f.Fake.Events.SequenceEqual(new[] { $"KD:{key:X2}", $"KU:{key:X2}" }), "Key order/release failed");
                Check(f.Fake.KeysHeld.Count == 0, "Key stuck");
            });
        }
        Test("keys/cancel-in-sequence", () =>
        {
            using var f = new Fixture(); f.Fake.Hook = p => { if (p == "KU:39") f.Cts.Cancel(); };
            Cancelled(() => { f.Tap(0x39); f.Tap(0x25); f.Tap(0x01); });
            Check(f.Fake.Events.SequenceEqual(new[] { "KD:39", "KU:39" }), "Additional independent input");
        });
        foreach (var checkpoint in new[] { "clipboard", "KD:1D", "KD:2F", "KU:2F" })
        {
            Test("paste/cancel-" + checkpoint, () =>
            {
                using var f = new Fixture(); f.Fake.Hook = p => { if (p == checkpoint) f.Cts.Cancel(); };
                Cancelled(() => f.Input.PasteText("강철괴", f.Cts.Token));
                string[] expected = checkpoint == "clipboard" ? [] : checkpoint == "KD:1D" ? ["clipboard", "KD:1D", "KU:1D"] : ["clipboard", "KD:1D", "KD:2F", "KU:2F", "KU:1D"];
                if (checkpoint == "clipboard") expected = ["clipboard"];
                Check(f.Fake.Events.SequenceEqual(expected), "Paste order/release failed"); Check(f.Fake.KeysHeld.Count == 0, "Paste key stuck");
            });
        }
        Test("drag/cancel-after-down", () =>
        {
            using var f = new Fixture(); f.Fake.Hook = p => { if (p == "MD") f.Cts.Cancel(); };
            Cancelled(() => f.Input.DragClientPoint(1, new Point(270, 330), new Point(520, 620), 800, f.Cts.Token));
            MouseBalanced(f.Fake, 1); Check(f.Fake.Moves == 1, "Drag continued after cancellation");
        });
        Test("drag/cancel-during-movement", () =>
        {
            using var f = new Fixture(); f.Fake.Hook = p => { if (p == "moved:3") f.Cts.Cancel(); };
            Cancelled(() => f.Input.DragClientPoint(1, new Point(270, 330), new Point(520, 620), 800, f.Cts.Token));
            MouseBalanced(f.Fake, 1); Check(f.Fake.Moves == 3, "Additional drag move");
        });
        foreach (var checkpoint in new[] { "MD", "wait:75", "KD:39", "KD:1D", "KD:2F" })
        {
            Test("exception/release-" + checkpoint, () =>
            {
                using var f = new Fixture(); f.Fake.Hook = p => { if (p == checkpoint) throw new InvalidOperationException("Injected driver/wait failure"); };
                Failed(() => { if (checkpoint.StartsWith("KD:39")) f.Tap(0x39); else if (checkpoint.StartsWith("KD:")) f.Input.PasteText("강철괴", f.Cts.Token); else f.Click(); });
                Check(!f.Fake.MouseHeld && f.Fake.KeysHeld.Count == 0, "Failed DOWN leaked pressed input");
            });
        }
        Test("release/failure-stops-future-input-and-releases-ctrl", () =>
        {
            using var f = new Fixture(); f.Fake.Hook = p => { if (p == "KU:2F") throw new InvalidOperationException("UP failed"); };
            Failed(() => f.Input.PasteText("강철괴", f.Cts.Token));
            Check(f.Fake.Events.Last() == "KU:1D", "Ctrl release skipped"); int count = f.Fake.Events.Count;
            Failed(() => f.Tap(0x39)); Check(f.Fake.Events.Count == count, "Input resumed after release failure");
        });
        Test("normal/click-exactly-once-with-original-delays", () =>
        {
            using var f = new Fixture(); f.Click(); MouseBalanced(f.Fake, 1);
            Check(f.Fake.Delays.SequenceEqual(new[] { 100, 70, 75, 30 }), "Click delays changed"); Check(f.Fake.LastMove == Target, "Click coordinates changed");
        });
        Test("normal/cursor-single-retry-and-delays", () =>
        {
            using var f = new Fixture(); f.Fake.CursorFailures = 1; f.Click(); MouseBalanced(f.Fake, 1);
            Check(f.Fake.Moves == 2 && f.Fake.Delays.SequenceEqual(new[] { 100, 70, 100, 75, 30 }), "Retry policy changed");
        });
        Test("normal/cursor-failure-no-down-no-third-retry", () =>
        {
            using var f = new Fixture(); f.Fake.CursorFailures = 2; Failed(f.Click); MouseBalanced(f.Fake, 0); Check(f.Fake.Moves == 2, "Retry count changed");
        });
        Test("normal/space-once-and-paste-order", () =>
        {
            using var f = new Fixture(); f.Tap(0x39);
            Check(f.Fake.Events.SequenceEqual(new[] { "KD:39", "KU:39" }) && f.Fake.Delays.SequenceEqual(new[] { 30 }), "Normal Space changed");
            f.Fake.Events.Clear(); f.Fake.Delays.Clear(); f.Input.PasteText("강철괴", f.Cts.Token);
            Check(f.Fake.Events.SequenceEqual(new[] { "clipboard", "KD:1D", "KD:2F", "KU:2F", "KU:1D" }) && f.Fake.Delays.SequenceEqual(new[] { 30, 30, 30, 80 }), "Normal Paste changed");
        });
        Test("normal/drag-delays-and-target", () =>
        {
            using var f = new Fixture(); f.Input.DragClientPoint(1, new Point(270, 330), new Point(520, 620), 800, f.Cts.Token);
            MouseBalanced(f.Fake, 1); Check(f.Fake.Moves == 21 && f.Fake.LastMove == new Point(520, 620), "Drag steps/coordinates changed");
            Check(f.Fake.Delays.SequenceEqual(new[] { 100, 90, 80 }.Concat(Enumerable.Repeat(40, 20)).Append(120)), "Drag delays changed");
        });
#if PRODUCTION_ASSEMBLY
        Test("clipboard/cancel-before-sta-worker-does-not-write", () =>
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            int writes = 0;
            Cancelled(() => CancellableClipboardWriter.Set("stale", cancelled.Token, _ => writes++));
            Check(writes == 0, "Cancelled clipboard worker wrote text");
        });
        Test("clipboard/cancel-during-sta-write-does-not-block-or-overwrite-newer", () =>
        {
            using var cancelled = new CancellationTokenSource();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var events = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var old = Task.Run(() => Cancelled(() => CancellableClipboardWriter.Set("old", cancelled.Token, _ =>
            {
                Check(Thread.CurrentThread.IsBackground, "Cancelled STA worker prevents process exit");
                Check(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "Worker lost STA apartment");
                events.Enqueue("old-start");
                entered.Set();
                Check(release.Wait(TimeSpan.FromSeconds(3)), "Old clipboard operation blocked");
                events.Enqueue("old-end");
            })));
            Check(entered.Wait(TimeSpan.FromSeconds(3)), "Old STA worker did not enter");
            try
            {
                cancelled.Cancel();
                Complete(old); // Cancellation must return while the STA worker remains blocked.
                var newer = Task.Run(() => CancellableClipboardWriter.Set("new", CancellationToken.None,
                    value => events.Enqueue(value)));
                Thread.Sleep(80);
                Check(!newer.IsCompleted && !events.Contains("new"), "New clipboard write overtook cancelled old writer");
                release.Set();
                Complete(newer);
                Check(events.SequenceEqual(new[] { "old-start", "old-end", "new" }),
                    "Cancelled old writer overwrote a newer clipboard value");
            }
            finally { release.Set(); }
        });
        Test("clipboard/cancelled-queued-sta-worker-cannot-publish-after-restart", () =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var cancelled = new CancellationTokenSource();
            var events = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var active = Task.Run(() => CancellableClipboardWriter.Set("active", CancellationToken.None, value =>
            {
                entered.Set();
                Check(release.Wait(TimeSpan.FromSeconds(3)), "Active clipboard writer stalled");
                events.Enqueue(value);
            }));
            Check(entered.Wait(TimeSpan.FromSeconds(3)), "Active clipboard writer missing");
            try
            {
                var queued = Task.Run(() => Cancelled(() => CancellableClipboardWriter.Set("stale", cancelled.Token,
                    value => events.Enqueue(value))));
                Thread.Sleep(60);
                cancelled.Cancel();
                Complete(queued); // Must not wait for the earlier blocked writer.
                release.Set();
                Complete(active);
                Complete(Task.Run(() => CancellableClipboardWriter.Set("new", CancellationToken.None,
                    value => events.Enqueue(value))));
                Check(events.SequenceEqual(new[] { "active", "new" }),
                    "Cancelled queued STA worker wrote stale clipboard text");
            }
            finally { release.Set(); }
        });
#endif
        GuardTests();
        Console.WriteLine($"N03: {passed} tests passed; fake transport only; no real-game verification.");
    }

    static void LockWait(bool guarded)
    {
        using var f = new Fixture(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var waiting = new ManualResetEventSlim();
        using var waitCts = new CancellationTokenSource();
        using var guard = new GuardedInputController(f.Input, f.Fake, f.Fake);
        guard.SetCancellation(f.Cts.Token); guard.ObserveFrame(1, new Size(800, 1000));
        f.Fake.Hook = p => { if (p == "lock-wait") waiting.Set(); if (p == "MD") { entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(3)), "Input blocked"); } };
        Action first = guarded ? () => guard.ClickClientPoint(1, Target) : f.Click;
        var holder = Task.Run(first); Check(entered.Wait(TimeSpan.FromSeconds(3)), "Lock holder not entered");
        var waiter = Task.Run(() => { Cancelled(() => { if (guarded) guard.ClickClientPoint(1, Target, waitCts.Token); else f.Input.ClickClientPoint(1, Target, waitCts.Token); }); });
        Check(waiting.Wait(TimeSpan.FromSeconds(3)), "Waiter not started");
        try { waitCts.Cancel(); Complete(waiter); } finally { release.Set(); }
        Complete(holder); MouseBalanced(f.Fake, 1);
    }

    static void GuardTests()
    {
        foreach (var violation in new[] { "point", "size", "position", "focus", "minimized", "invalidated", "stale", "wrong-window", "unbound" })
        {
            Test("guard/reject-" + violation, () =>
            {
                using var f = new Fixture(); using var g = new GuardedInputController(f.Input, f.Fake, f.Fake);
                if (violation != "unbound") g.SetCancellation(f.Cts.Token);
                g.ObserveFrame(1, new Size(800, 1000));
                if (violation == "size") f.Fake.RequiredSize = false;
                if (violation == "position") f.Fake.Origin = new Point(10, 0);
                if (violation == "focus") f.Fake.Foreground = false;
                if (violation == "minimized") f.Fake.Minimized = true;
                if (violation == "invalidated") g.Invalidate();
                if (violation == "stale") typeof(GuardedInputController).GetField("_observedAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(g, Environment.TickCount64 - 5001);
                Action click = () => g.ClickClientPoint(violation == "wrong-window" ? 2 : 1, violation == "point" ? new Point(800, 1000) : Target);
                if (violation == "unbound") Failed(click); else Cancelled(click);
                MouseBalanced(f.Fake, 0);
                if (violation == "focus") Check(f.Fake.Delays.SequenceEqual(new[] { 80 }.Concat(Enumerable.Repeat(120, 7))), "Focus retry/delays changed");
            });
        }
        Test("guard/normal-focus-retry-and-click", () =>
        {
            using var f = new Fixture(); using var g = new GuardedInputController(f.Input, f.Fake, f.Fake);
            g.SetCancellation(f.Cts.Token); g.ObserveFrame(1, new Size(800, 1000));
            f.Fake.Foreground = false;
            f.Fake.Hook = p => { if (p == "wait:80") f.Fake.Foreground = true; };
            g.ClickClientPoint(1, Target);
            MouseBalanced(f.Fake, 1);
            Check(f.Fake.Delays.SequenceEqual(new[] { 80, 100, 70, 75, 30 }), "Normal guarded focus/click changed");
        });
        Test("guard/reject-bad-capture-size", () =>
        {
            using var f = new Fixture(); using var g = new GuardedInputController(f.Input, f.Fake, f.Fake);
            g.SetCancellation(f.Cts.Token); Cancelled(() => g.ObserveFrame(1, new Size(801, 1000))); MouseBalanced(f.Fake, 0);
        });
        Test("guard/bound-token-legacy-call-and-rearm-blocked", () =>
        {
            using var f = new Fixture(); using var g = new GuardedInputController(f.Input, f.Fake, f.Fake);
            g.SetCancellation(f.Cts.Token); g.ObserveFrame(1, new Size(800, 1000));
            g.TapScanCode(0x39); f.Cts.Cancel(); Cancelled(() => g.TapScanCode(0x39));
            using var cleanup = new CancellationTokenSource(); Cancelled(() => g.SetCancellation(cleanup.Token));
            Check(f.Fake.Events.SequenceEqual(new[] { "KD:39", "KU:39" }), "Legacy path bypassed cancellation");
        });
    }

    sealed class Fixture : IDisposable
    {
        public readonly CancellationTokenSource Cts = new();
        public readonly Fake Fake = new();
        public readonly IInputController Input;
        public Fixture(bool realTiming = false)
        {
            IInputTiming timing = realTiming ? InputTiming.Instance : Fake;
#if PRODUCTION_ASSEMBLY
            Input = new InterceptionInput(Fake, Fake, timing);
#else
            Input = new PortableInput(Fake, timing);
#endif
        }
        public void Click() => Input.ClickClientPoint(1, Target, Cts.Token);
        public void Tap(ushort key) => Input.TapScanCode(key, Cts.Token);
        public void Dispose() { Input.Dispose(); Cts.Dispose(); }
    }
#if !PRODUCTION_ASSEMBLY
    sealed class PortableInput : IInputController
    {
        readonly CancellableInputSequence sequence;
        public PortableInput(Fake fake, IInputTiming timing) => sequence = new(fake, fake, timing, new object());
        public string ModeName => "Portable production sequence";
        public void ClickClientPoint(nint hwnd, Point p, CancellationToken ct) => sequence.Click(hwnd, p, ct);
        public void DragClientPoint(nint hwnd, Point a, Point b, int ms, CancellationToken ct) => sequence.Drag(hwnd, a, b, ms, ct);
        public void TapScanCode(ushort code, CancellationToken ct) => sequence.Tap(code, ct);
        public void PasteText(string text, CancellationToken ct) => sequence.Paste(text, ct);
        public void Dispose() => sequence.Dispose(() => { });
    }
#endif
    sealed class ForwardingInput(IInputController inner, Action before) : IInputController
    {
        public string ModeName => inner.ModeName;
        public void ClickClientPoint(nint hwnd, Point p, CancellationToken ct) { before(); inner.ClickClientPoint(hwnd, p, ct); }
        public void DragClientPoint(nint hwnd, Point a, Point b, int ms, CancellationToken ct) { before(); inner.DragClientPoint(hwnd, a, b, ms, ct); }
        public void TapScanCode(ushort code, CancellationToken ct) { before(); inner.TapScanCode(code, ct); }
        public void PasteText(string text, CancellationToken ct) { before(); inner.PasteText(text, ct); }
        public void Dispose() { }
    }
    sealed class Fake : IInputTransport, IInputWindow, IInputTiming
    {
        public List<string> Events = new(); public List<int> Delays = new();
        public HashSet<ushort> KeysHeld = new(); public bool MouseHeld;
        public Action<string>? Hook; public int CursorFailures, Moves, CursorReads;
        public Point LastMove, Origin; public bool RequiredSize = true, Foreground = true, Minimized;
        void At(string point) => Hook?.Invoke(point);
        void Event(string point) { Events.Add(point); At(point); }
        public void Move(Point p) { LastMove = p; Moves++; Event("move"); At("moved:" + Moves); }
        public void MouseButton(bool down) { MouseHeld = down; Event(down ? "MD" : "MU"); }
        public void Key(ushort code, bool down) { if (down) KeysHeld.Add(code); else KeysHeld.Remove(code); Event($"{(down ? "KD" : "KU")}:{code:X2}"); }
        public void Clipboard(string text, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Event("clipboard"); }
        public void Wait(int ms, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Delays.Add(ms); At("wait:" + ms); ct.ThrowIfCancellationRequested(); }
        public void WaitingForLock() => At("lock-wait");
        public bool IsIconic(nint hwnd) => Minimized;
        public bool TryOrigin(nint hwnd, out Point p) { p = Origin; return true; }
        public bool HasRequiredSize(nint hwnd) => RequiredSize;
        public bool IsForeground(nint hwnd) => Foreground;
        public void Activate(nint hwnd) => At("activated");
        public bool TryCursor(out Point p) { p = LastMove; bool ok = CursorReads++ >= CursorFailures; At("cursor:" + CursorReads); return ok; }
    }
}
