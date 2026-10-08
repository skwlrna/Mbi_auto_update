using System.Drawing;

namespace DungeonVisionBot;

// Shared gate also covers different runner instances during cancellation/disposal.
internal sealed class GuardedInputController : IInputController
{
    private static readonly object Gate = new();
    private readonly IInputController _inner;
    private sealed record CancellationBinding(CancellationToken Token);
    private CancellationBinding? _binding;
    private CancellationToken _ct => Volatile.Read(ref _binding)?.Token ?? default;
    private readonly IInputWindow _environment;
    private readonly IInputTiming _timing;
    private nint _window;
    private Point _origin;
    private long _observedAt;
    private bool _disposed;
    public string ModeName => _inner.ModeName + ", frame-guarded";
    public GuardedInputController(IInputController inner)
        : this(inner, NativeInputWindow.Instance, InputTiming.Instance) { }
    internal GuardedInputController(IInputController inner, IInputWindow environment, IInputTiming timing)
    { _inner = inner; _environment = environment; _timing = timing; }
    public void SetCancellation(CancellationToken ct)
    {
        using var held = InputLock.Enter(Gate, ct);
        // A cancelled runtime is terminal. Cleanup tokens may authorize read-only
        // verification, but must not re-arm its input after F10.
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ct.ThrowIfCancellationRequested();
        Volatile.Write(ref _binding, new CancellationBinding(ct));
    }
    public void Invalidate() { lock (Gate) _observedAt = 0; }
    public void ObserveFrame(nint hwnd, Size size)
    {
        lock (Gate)
        {
            _ct.ThrowIfCancellationRequested();
            _observedAt = 0;
            if (size.Width != 800 || size.Height != 1000 || _environment.IsIconic(hwnd))
                throw new OperationCanceledException("게임 화면이 800×1000이 아니거나 최소화되어 입력을 정지합니다.");
            if (!_environment.TryOrigin(hwnd, out var origin))
                throw new OperationCanceledException("게임 화면 위치 확인 실패 -> 입력 정지");
            _window = hwnd; _origin = new Point(origin.X, origin.Y);
            _observedAt = Environment.TickCount64;
        }
    }
    private void Verify(nint hwnd, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_disposed || hwnd == 0 || hwnd != _window || _observedAt == 0 ||
            Environment.TickCount64 - _observedAt > 5000)
            throw new OperationCanceledException("입력 직전 화면이 오래되었거나 실행이 종료되어 정지합니다.");
        if (!_environment.HasRequiredSize(hwnd) ||
            _environment.IsIconic(hwnd) || !_environment.TryOrigin(hwnd, out var origin) ||
            origin.X != _origin.X || origin.Y != _origin.Y)
            throw new OperationCanceledException("화면 인식 후 게임 창 위치/크기가 바뀌어 입력을 정지합니다.");
        if (!EnsureGameForeground(hwnd, ct))
            throw new OperationCanceledException("게임 창 포커스 확인 실패 -> 입력 정지");
        ct.ThrowIfCancellationRequested();
    }

    // V0.1.77: Windows/remote-control environments can take a short moment to
    // complete foreground activation. Keep the safety check, but retry briefly
    // instead of stopping on the very first foreground read.
    private bool EnsureGameForeground(nint hwnd, CancellationToken ct)
    {
        const int attempts = 8;
        for (int i = 0; i < attempts; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (_environment.IsForeground(hwnd))
                return true;

            _environment.Activate(hwnd);
            ct.ThrowIfCancellationRequested();
            _timing.Wait(i == 0 ? 80 : 120, ct);
            ct.ThrowIfCancellationRequested();

            if (_environment.IsForeground(hwnd))
                return true;
        }

        return false;
    }
    // Legacy dungeon calls use the bound run token; an unbound call fails closed.
    // Explicit action tokens are linked, so neither path can bypass run cancellation.
    private CancellationTokenSource InputCancellation(CancellationToken ct)
    {
        var binding = Volatile.Read(ref _binding)
            ?? throw new InvalidOperationException("입력 취소 토큰이 설정되지 않았습니다.");
        return CancellationTokenSource.CreateLinkedTokenSource(binding.Token, ct);
    }
    public void ClickClientPoint(nint hwnd, Point point, CancellationToken ct = default)
    {
        using var linked = InputCancellation(ct);
        using var held = InputLock.Enter(Gate, linked.Token, _timing.WaitingForLock);
        Verify(hwnd, linked.Token); CheckPoint(point);
        _inner.ClickClientPoint(hwnd, point, linked.Token);
    }
    public void DragClientPoint(nint hwnd, Point start, Point end, int durationMs, CancellationToken ct = default)
    {
        using var linked = InputCancellation(ct);
        using var held = InputLock.Enter(Gate, linked.Token, _timing.WaitingForLock);
        Verify(hwnd, linked.Token); CheckPoint(start); CheckPoint(end);
        _inner.DragClientPoint(hwnd, start, end, durationMs, linked.Token);
    }
    public void TapScanCode(ushort code, CancellationToken ct = default)
    {
        using var linked = InputCancellation(ct);
        using var held = InputLock.Enter(Gate, linked.Token, _timing.WaitingForLock);
        Verify(_window, linked.Token); _inner.TapScanCode(code, linked.Token);
    }
    public void PasteText(string text, CancellationToken ct = default)
    {
        using var linked = InputCancellation(ct);
        using var held = InputLock.Enter(Gate, linked.Token, _timing.WaitingForLock);
        Verify(_window, linked.Token); _inner.PasteText(text, linked.Token);
    }
    private static void CheckPoint(Point p)
    {
        if (p.X < 0 || p.Y < 0 || p.X >= 800 || p.Y >= 1000)
            throw new OperationCanceledException("게임 영역 밖 좌표 -> 입력 정지");
    }
    public void Dispose()
    {
        lock (Gate) { if (_disposed) return; _disposed = true; _observedAt = 0; _inner.Dispose(); }
    }
}
