using System.Runtime.InteropServices;

namespace DungeonVisionBot;

internal sealed class InterceptionInput : IInputController, IInputTransport
{
    private readonly nint _context;
    private readonly int _mouseDevice;
    private readonly int _keyboardDevice;
    // V0163_GLOBAL_INPUT_LOCK: macro-generated mouse/keyboard input shares one gate.
    // This does not block the user's physical input; it only serializes automation sends.
    private readonly object _inputGate = new();
    private readonly CancellableInputSequence _sequence;

    public string ModeName =>
        $"Interception(mouse={_mouseDevice}, keyboard={_keyboardDevice}, verified-cursor, input-locked)";

    public InterceptionInput(int preferredMouseDevice, int keyboardDevice)
    {
        _sequence = new CancellableInputSequence(this, NativeInputWindow.Instance, InputTiming.Instance, _inputGate);
        _keyboardDevice = keyboardDevice;

        _context = interception_create_context();
        if (_context == 0)
            throw new InvalidOperationException(
                "Interception context 생성 실패. Interception 드라이버 설치 상태를 확인하세요.");

        try
        {
            _mouseDevice = FindWorkingMouseDevice(preferredMouseDevice);

            if (_mouseDevice == 0)
            {
                throw new InvalidOperationException(
                    "동작하는 Interception 마우스 장치를 찾지 못했습니다. " +
                    "Interception 드라이버를 관리자 권한으로 설치한 뒤 Windows를 재부팅하세요.");
            }
        }
        catch
        {
            interception_destroy_context(_context);
            throw;
        }
    }

    // Test-only dependency injection: this constructor performs no native calls.
    internal InterceptionInput(IInputTransport transport, IInputWindow window, IInputTiming timing)
        => _sequence = new CancellableInputSequence(transport, window, timing, _inputGate);

    public void ClickClientPoint(nint hwnd, Point clientPoint, CancellationToken ct)
        => _sequence.Click(hwnd, clientPoint, ct);
    public void DragClientPoint(nint hwnd, Point startClientPoint, Point endClientPoint, int durationMs, CancellationToken ct)
        => _sequence.Drag(hwnd, startClientPoint, endClientPoint, durationMs, ct);
    public void TapScanCode(ushort scanCode, CancellationToken ct) => _sequence.Tap(scanCode, ct);
    public void PasteText(string text, CancellationToken ct) => _sequence.Paste(text, ct);

    void IInputTransport.Move(Point point) => SendAbsolute(_mouseDevice, point.X, point.Y);
    void IInputTransport.MouseButton(bool down)
        => SendMouse(_mouseDevice, new InterceptionMouseStroke { state = down ? MOUSE_LEFT_DOWN : MOUSE_LEFT_UP });
    void IInputTransport.Key(ushort scanCode, bool down)
    {
        var stroke = new InterceptionKeyStroke { code = scanCode, state = down ? (ushort)0 : KEY_UP };
        if (interception_send(_context, _keyboardDevice, ref stroke, 1) <= 0)
            throw new InvalidOperationException($"Interception keyboard device {_keyboardDevice} 입력 전송 실패");
    }
    void IInputTransport.Clipboard(string text, CancellationToken ct) => SetClipboardText(text, ct);

    private static void SetClipboardText(string text, CancellationToken ct)
    {
        // The STA worker is background and shared writes are serialized across
        // cancelled/restarted runs. A late old write cannot overwrite a newer
        // successful paste. Cancellation never waits for a blocked STA worker.
        CancellableClipboardWriter.Set(text, ct, System.Windows.Forms.Clipboard.SetText);
    }

    private int FindWorkingMouseDevice(int preferred)
    {
        var candidates = new List<int>();

        if (preferred >= 11 && preferred <= 20)
            candidates.Add(preferred);

        for (int device = 11; device <= 20; device++)
        {
            if (!candidates.Contains(device))
                candidates.Add(device);
        }

        if (!GetCursorPos(out var original))
            throw new InvalidOperationException("현재 마우스 커서 위치를 읽지 못했습니다.");

        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        // Move only a few pixels and restore immediately.
        int probeX = Math.Clamp(
            original.X < vx + vw - 12 ? original.X + 8 : original.X - 8,
            vx,
            vx + Math.Max(0, vw - 1));

        int probeY = Math.Clamp(
            original.Y,
            vy,
            vy + Math.Max(0, vh - 1));

        foreach (int device in candidates)
        {
            try
            {
                int sent = SendAbsoluteRaw(device, probeX, probeY);
                if (sent <= 0)
                    continue;

                Thread.Sleep(45);

                if (!GetCursorPos(out var moved))
                    continue;

                bool movedCorrectly =
                    Math.Abs(moved.X - probeX) <= 5 &&
                    Math.Abs(moved.Y - probeY) <= 5;

                if (!movedCorrectly)
                    continue;

                // Restore the user's cursor with the same verified device.
                SendAbsoluteRaw(device, original.X, original.Y);
                Thread.Sleep(45);

                return device;
            }
            catch
            {
                // Try the next mouse device.
            }
        }

        return 0;
    }

    private void SendAbsolute(int device, int screenX, int screenY)
    {
        int sent = SendAbsoluteRaw(device, screenX, screenY);

        if (sent <= 0)
            throw new InvalidOperationException(
                $"Interception mouse device {device} 이동 입력 전송 실패");
    }

    private int SendAbsoluteRaw(int device, int screenX, int screenY)
    {
        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        int nx = (int)Math.Round(
            (screenX - vx) * 65535.0 / Math.Max(1, vw - 1));

        int ny = (int)Math.Round(
            (screenY - vy) * 65535.0 / Math.Max(1, vh - 1));

        nx = Math.Clamp(nx, 0, 65535);
        ny = Math.Clamp(ny, 0, 65535);

        var move = new InterceptionMouseStroke
        {
            flags = MOUSE_MOVE_ABSOLUTE | MOUSE_VIRTUAL_DESKTOP,
            x = nx,
            y = ny
        };

        return interception_send(
            _context,
            device,
            ref move,
            1);
    }

    private void SendMouse(
        int device,
        InterceptionMouseStroke stroke)
    {
        int sent = interception_send(
            _context,
            device,
            ref stroke,
            1);

        if (sent <= 0)
            throw new InvalidOperationException(
                $"Interception mouse device {device} 클릭 입력 전송 실패");
    }

    public void Dispose() => _sequence.Dispose(() =>
    {
        if (_context != 0) interception_destroy_context(_context);
    });

    private const ushort MOUSE_LEFT_DOWN = 0x001;
    private const ushort MOUSE_LEFT_UP = 0x002;
    private const ushort MOUSE_MOVE_ABSOLUTE = 0x001;
    private const ushort MOUSE_VIRTUAL_DESKTOP = 0x002;
    private const ushort KEY_UP = 0x001;

    [StructLayout(LayoutKind.Sequential)]
    private struct InterceptionMouseStroke
    {
        public ushort state;
        public ushort flags;
        public short rolling;
        public int x;
        public int y;
        public uint information;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InterceptionKeyStroke
    {
        public ushort code;
        public ushort state;
        public uint information;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out WinPoint lpPoint);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint interception_create_context();

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void interception_destroy_context(nint context);

    [DllImport(
        "interception.dll",
        CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "interception_send")]
    private static extern int interception_send(
        nint context,
        int device,
        ref InterceptionMouseStroke stroke,
        uint nstroke);

    [DllImport(
        "interception.dll",
        CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "interception_send")]
    private static extern int interception_send(
        nint context,
        int device,
        ref InterceptionKeyStroke stroke,
        uint nstroke);
}

// Kept only so older code still compiles. The automation no longer
// silently falls back to this mode when Interception is requested.
internal sealed class SendInputFallback : IInputController
{
    public string ModeName => "Windows SendInput (fallback)";

    public void ClickClientPoint(nint hwnd, Point clientPoint, CancellationToken ct)
    {
        throw new InvalidOperationException(
            "SendInput fallback은 비활성화되어 있습니다. " +
            "게임 클릭은 Interception으로만 전송합니다.");
    }

    public void DragClientPoint(nint hwnd, Point startClientPoint, Point endClientPoint, int durationMs, CancellationToken ct)
    {
        throw new InvalidOperationException(
            "SendInput fallback은 비활성화되어 있습니다. 지도 드래그는 Interception으로만 전송합니다.");
    }

    public void TapScanCode(ushort scanCode, CancellationToken ct)
    {
        throw new InvalidOperationException(
            "SendInput fallback은 비활성화되어 있습니다.");
    }

    public void PasteText(string text, CancellationToken ct)
    {
        throw new InvalidOperationException(
            "SendInput fallback은 비활성화되어 있습니다.");
    }

    public void Dispose() { }
}
