using System.Drawing;
using System.Runtime.InteropServices;

namespace DungeonVisionBot;

// Shared gate also covers different runner instances during cancellation/disposal.
internal sealed class GuardedInputController : IInputController
{
    private static readonly object Gate = new();
    private readonly IInputController _inner;
    private CancellationToken _ct;
    private nint _window;
    private Point _origin;
    private long _observedAt;
    private bool _disposed;
    public string ModeName => _inner.ModeName + ", frame-guarded";
    public GuardedInputController(IInputController inner) => _inner = inner;
    public void SetCancellation(CancellationToken ct) => _ct = ct;
    public void Invalidate() { lock (Gate) _observedAt = 0; }
    public void ObserveFrame(nint hwnd, Size size)
    {
        lock (Gate)
        {
            _ct.ThrowIfCancellationRequested();
            _observedAt = 0;
            if (size.Width != 800 || size.Height != 1000 || NativeMethods.IsIconic(hwnd))
                throw new OperationCanceledException("게임 화면이 800×1000이 아니거나 최소화되어 입력을 정지합니다.");
            var origin = new NativeMethods.POINT();
            if (!NativeMethods.ClientToScreen(hwnd, ref origin))
                throw new OperationCanceledException("게임 화면 위치 확인 실패 -> 입력 정지");
            _window = hwnd; _origin = new Point(origin.X, origin.Y);
            _observedAt = Environment.TickCount64;
        }
    }
    private void Verify(nint hwnd)
    {
        _ct.ThrowIfCancellationRequested();
        if (_disposed || hwnd == 0 || hwnd != _window || _observedAt == 0 ||
            Environment.TickCount64 - _observedAt > 5000)
            throw new OperationCanceledException("입력 직전 화면이 오래되었거나 실행이 종료되어 정지합니다.");
        var origin = new NativeMethods.POINT();
        if (!NativeMethods.GetClientRect(hwnd, out var rect) || rect.Right != 800 || rect.Bottom != 1000 ||
            NativeMethods.IsIconic(hwnd) || !NativeMethods.ClientToScreen(hwnd, ref origin) ||
            origin.X != _origin.X || origin.Y != _origin.Y)
            throw new OperationCanceledException("화면 인식 후 게임 창 위치/크기가 바뀌어 입력을 정지합니다.");
        if (!EnsureGameForeground(hwnd))
            throw new OperationCanceledException("게임 창 포커스 확인 실패 -> 입력 정지");
        _ct.ThrowIfCancellationRequested();
    }

    // V0.1.77: Windows/remote-control environments can take a short moment to
    // complete foreground activation. Keep the safety check, but retry briefly
    // instead of stopping on the very first foreground read.
    private bool EnsureGameForeground(nint hwnd)
    {
        const int attempts = 8;
        for (int i = 0; i < attempts; i++)
        {
            _ct.ThrowIfCancellationRequested();

            if (GetForegroundWindow() == hwnd)
                return true;

            NativeMethods.SetForegroundWindow(hwnd);
            Thread.Sleep(i == 0 ? 80 : 120);

            if (GetForegroundWindow() == hwnd)
                return true;
        }

        return false;
    }
    public void ClickClientPoint(nint hwnd, Point point)
    {
        lock (Gate) { Verify(hwnd); CheckPoint(point); _inner.ClickClientPoint(hwnd, point); }
    }
    public void DragClientPoint(nint hwnd, Point start, Point end, int durationMs)
    {
        lock (Gate) { Verify(hwnd); CheckPoint(start); CheckPoint(end); _inner.DragClientPoint(hwnd, start, end, durationMs); }
    }
    public void TapScanCode(ushort code) { lock (Gate) { Verify(_window); _inner.TapScanCode(code); } }
    private static void CheckPoint(Point p)
    {
        if (p.X < 0 || p.Y < 0 || p.X >= 800 || p.Y >= 1000)
            throw new OperationCanceledException("게임 영역 밖 좌표 -> 입력 정지");
    }
    public void Dispose()
    {
        lock (Gate) { if (_disposed) return; _disposed = true; _observedAt = 0; _inner.Dispose(); }
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
