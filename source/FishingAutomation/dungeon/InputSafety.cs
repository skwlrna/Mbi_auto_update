using System.Drawing;

namespace DungeonVisionBot;

internal interface IInputController : IDisposable
{
    string ModeName { get; }
    void ClickClientPoint(nint hwnd, Point clientPoint, CancellationToken ct);
    void DragClientPoint(nint hwnd, Point startClientPoint, Point endClientPoint, int durationMs, CancellationToken ct);
    void TapScanCode(ushort scanCode, CancellationToken ct);
    void PasteText(string text, CancellationToken ct);
}

// Native calls live behind these seams. Tests run the production sequencer with
// fake transports/windows; they never initialize Interception or send OS input.
internal interface IInputTransport
{
    void Move(Point screenPoint);
    void MouseButton(bool down);
    void Key(ushort scanCode, bool down);
    void Clipboard(string text, CancellationToken ct);
}

internal interface IInputWindow
{
    bool IsIconic(nint hwnd);
    bool TryOrigin(nint hwnd, out Point origin);
    bool HasRequiredSize(nint hwnd);
    bool IsForeground(nint hwnd);
    void Activate(nint hwnd);
    bool TryCursor(out Point cursor);
}

internal interface IInputTiming
{
    void Wait(int milliseconds, CancellationToken ct);
    void WaitingForLock() { }
}

internal sealed class InputTiming : IInputTiming
{
    internal static readonly InputTiming Instance = new();
    public void Wait(int milliseconds, CancellationToken ct)
    {
        if (ct.WaitHandle.WaitOne(milliseconds)) ct.ThrowIfCancellationRequested();
    }
}

internal static class InputLock
{
    internal static IDisposable Enter(object gate, CancellationToken ct, Action? waiting = null)
    {
        ct.ThrowIfCancellationRequested();
        while (!Monitor.TryEnter(gate, 20)) { waiting?.Invoke(); ct.ThrowIfCancellationRequested(); }
        try { ct.ThrowIfCancellationRequested(); return new Lease(gate); }
        catch { Monitor.Exit(gate); throw; }
    }
    private sealed class Lease(object gate) : IDisposable
    {
        public void Dispose() => Monitor.Exit(gate);
    }
}

// Cancellation invalidates future send admission with one atomic exchange; it
// never waits on the input/window/driver lock. CAS 0->2 is the send's admission
// point. A send admitted first is in flight and cannot be recalled from native
// Interception. There are no waits or window work between admission and send.
internal sealed class InputSendPermit : IDisposable
{
    private readonly CancellationToken _ct;
    private readonly CancellationTokenRegistration _registration;
    private int _state; // 0=open, 1=cancelled, 2=one admitted send
    internal InputSendPermit(CancellationToken ct)
    {
        _ct = ct;
        _registration = ct.Register(() => Interlocked.Exchange(ref _state, 1));
    }
    internal void Send(Action send)
    {
        _ct.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _state, 2, 0) != 0)
            throw new OperationCanceledException(_ct);
        try
        {
            // Also observe a token whose callback has not yet been dispatched.
            _ct.ThrowIfCancellationRequested();
            send();
        }
        finally { Interlocked.CompareExchange(ref _state, 0, 2); }
    }
    public void Dispose() => _registration.Dispose();
}

internal sealed class CancellableInputSequence(
    IInputTransport transport, IInputWindow window, IInputTiming timing, object gate)
{
    private bool _disposed;
    private bool _releaseFailed;
    private void Ready(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_releaseFailed)
            throw new InvalidOperationException("필수 입력 해제 실패 -> 추가 입력을 정지합니다.");
    }
    private Point Origin(nint hwnd, CancellationToken ct)
    {
        Ready(ct);
        if (hwnd == 0 || !window.TryOrigin(hwnd, out var origin))
            throw new InvalidOperationException("게임 창 좌표를 화면 좌표로 변환하지 못했습니다.");
        return origin;
    }
    private void Wait(int ms, CancellationToken ct) { timing.Wait(ms, ct); ct.ThrowIfCancellationRequested(); }
    private void Move(InputSendPermit permit, Point point) => permit.Send(() => transport.Move(point));
    private void Release(Action up)
    {
        try { up(); }
        catch { _releaseFailed = true; throw; }
    }
    private void MouseDown(InputSendPermit permit, ref bool attempted)
    {
        bool started = false;
        try { permit.Send(() => { started = true; transport.MouseButton(true); }); }
        finally { attempted = started; }
    }
    private void KeyDown(InputSendPermit permit, ushort code, ref bool attempted)
    {
        bool started = false;
        try { permit.Send(() => { started = true; transport.Key(code, true); }); }
        finally { attempted = started; }
    }
    private bool CursorAt(Point point, int tolerance)
        => window.TryCursor(out var actual) && Math.Abs(actual.X - point.X) <= tolerance && Math.Abs(actual.Y - point.Y) <= tolerance;

    internal void Click(nint hwnd, Point point, CancellationToken ct)
    {
        using var held = InputLock.Enter(gate, ct, timing.WaitingForLock);
        var origin = Origin(hwnd, ct);
        using var permit = new InputSendPermit(ct);
        var target = new Point(origin.X + point.X, origin.Y + point.Y);
        ct.ThrowIfCancellationRequested();
        window.Activate(hwnd);
        ct.ThrowIfCancellationRequested();
        Wait(100, ct);
        Move(permit, target);
        ct.ThrowIfCancellationRequested();
        Wait(70, ct);
        if (!CursorAt(target, 5))
        {
            Move(permit, target);
            ct.ThrowIfCancellationRequested();
            Wait(100, ct);
            if (!CursorAt(target, 5))
                throw new InvalidOperationException("Interception 입력은 전송했지만 실제 커서가 목표 위치로 이동하지 않았습니다.");
        }
        ct.ThrowIfCancellationRequested();
        bool down = false;
        try { MouseDown(permit, ref down); Wait(75, ct); }
        finally { if (down) Release(() => transport.MouseButton(false)); }
        Wait(30, ct);
    }

    internal void Drag(nint hwnd, Point start, Point end, int durationMs, CancellationToken ct)
    {
        using var held = InputLock.Enter(gate, ct, timing.WaitingForLock);
        var origin = Origin(hwnd, ct);
        using var permit = new InputSendPermit(ct);
        var from = new Point(origin.X + start.X, origin.Y + start.Y);
        var to = new Point(origin.X + end.X, origin.Y + end.Y);
        ct.ThrowIfCancellationRequested();
        window.Activate(hwnd);
        ct.ThrowIfCancellationRequested();
        Wait(100, ct);
        Move(permit, from);
        ct.ThrowIfCancellationRequested();
        Wait(90, ct);
        bool down = false;
        try
        {
            MouseDown(permit, ref down);
            Wait(80, ct);
            int steps = Math.Clamp(Math.Max(8, durationMs / 40), 8, 30);
            int delay = Math.Max(15, durationMs / steps);
            for (int i = 1; i <= steps; i++)
            {
                double t = i / (double)steps;
                Move(permit, new Point((int)Math.Round(from.X + (to.X - from.X) * t), (int)Math.Round(from.Y + (to.Y - from.Y) * t)));
                ct.ThrowIfCancellationRequested();
                Wait(delay, ct);
            }
        }
        finally { if (down) Release(() => transport.MouseButton(false)); }
        Wait(120, ct);
        if (!CursorAt(to, 8)) throw new InvalidOperationException("Interception 드래그 후 커서 검증 실패");
    }

    internal void Tap(ushort code, CancellationToken ct)
    {
        using var held = InputLock.Enter(gate, ct, timing.WaitingForLock);
        Ready(ct);
        using var permit = new InputSendPermit(ct);
        bool down = false;
        try { KeyDown(permit, code, ref down); Wait(30, ct); }
        finally { if (down) Release(() => transport.Key(code, false)); }
        ct.ThrowIfCancellationRequested();
    }

    internal void Paste(string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 256 || text.Any(char.IsControl))
            throw new InvalidOperationException("붙여넣을 검색어가 올바르지 않습니다.");
        using var held = InputLock.Enter(gate, ct, timing.WaitingForLock);
        Ready(ct);
        using var permit = new InputSendPermit(ct);
        transport.Clipboard(text, ct);
        ct.ThrowIfCancellationRequested();
        bool ctrl = false, v = false;
        try
        {
            KeyDown(permit, 0x1D, ref ctrl);
            Wait(30, ct);
            KeyDown(permit, 0x2F, ref v);
            Wait(30, ct);
            // Clear only after successful release; on a failed UP fail closed.
            Release(() => transport.Key(0x2F, false)); v = false;
            Wait(30, ct);
        }
        finally
        {
            // Even a failing V release must not skip Ctrl release.
            try { if (v && !_releaseFailed) Release(() => transport.Key(0x2F, false)); }
            finally { if (ctrl) Release(() => transport.Key(0x1D, false)); }
        }
        Wait(80, ct);
    }

    internal void Dispose(Action dispose)
    {
        lock (gate) { if (_disposed) return; _disposed = true; dispose(); }
    }
}
