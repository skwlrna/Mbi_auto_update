using System.Runtime.InteropServices;

namespace DungeonVisionBot;

internal interface IInputController : IDisposable
{
    string ModeName { get; }
    void ClickClientPoint(nint hwnd, Point clientPoint);
    void DragClientPoint(nint hwnd, Point startClientPoint, Point endClientPoint, int durationMs);
    void TapScanCode(ushort scanCode);
}

internal sealed class InterceptionInput : IInputController
{
    private readonly nint _context;
    private readonly int _mouseDevice;
    private readonly int _keyboardDevice;
    // V0163_GLOBAL_INPUT_LOCK: macro-generated mouse/keyboard input shares one gate.
    // This does not block the user's physical input; it only serializes automation sends.
    private readonly object _inputGate = new();
    private bool _disposed;

    public string ModeName =>
        $"Interception(mouse={_mouseDevice}, keyboard={_keyboardDevice}, verified-cursor, input-locked)";

    public InterceptionInput(int preferredMouseDevice, int keyboardDevice)
    {
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

    public void ClickClientPoint(nint hwnd, Point clientPoint)
    {
        lock (_inputGate)
        {
            try { ClickClientPointCore(hwnd, clientPoint); }
            finally { SendMouse(_mouseDevice, new InterceptionMouseStroke { state = MOUSE_LEFT_UP }); }
        }
    }

    private void ClickClientPointCore(nint hwnd, Point clientPoint)
    {
        if (hwnd == 0)
            throw new InvalidOperationException("게임 창 핸들이 없습니다.");

        var origin = new NativeMethods.POINT { X = 0, Y = 0 };

        if (!NativeMethods.ClientToScreen(hwnd, ref origin))
            throw new InvalidOperationException("게임 창 좌표를 화면 좌표로 변환하지 못했습니다.");

        int sx = origin.X + clientPoint.X;
        int sy = origin.Y + clientPoint.Y;

        // The click is only useful when Mabinogi Mobile is foreground.
        NativeMethods.SetForegroundWindow(hwnd);
        Thread.Sleep(100);

        // Move with the selected Interception mouse device.
        SendAbsolute(_mouseDevice, sx, sy);
        Thread.Sleep(70);

        // Verify that the driver-level move actually reached Windows.
        if (!GetCursorPos(out var actual) ||
            Math.Abs(actual.X - sx) > 5 ||
            Math.Abs(actual.Y - sy) > 5)
        {
            // One retry is useful when the game/window has just changed focus.
            SendAbsolute(_mouseDevice, sx, sy);
            Thread.Sleep(100);

            if (!GetCursorPos(out actual) ||
                Math.Abs(actual.X - sx) > 5 ||
                Math.Abs(actual.Y - sy) > 5)
            {
                throw new InvalidOperationException(
                    $"Interception 입력은 전송했지만 실제 커서가 목표 위치로 이동하지 않았습니다. " +
                    $"device={_mouseDevice}, target=({sx},{sy}), " +
                    $"cursor=({actual.X},{actual.Y}).");
            }
        }

        SendMouse(_mouseDevice, new InterceptionMouseStroke
        {
            state = MOUSE_LEFT_DOWN
        });

        // A slightly longer physical-style press is more reliable in games.
        Thread.Sleep(75);

        SendMouse(_mouseDevice, new InterceptionMouseStroke
        {
            state = MOUSE_LEFT_UP
        });

        Thread.Sleep(30);
    }

    public void DragClientPoint(nint hwnd, Point startClientPoint, Point endClientPoint, int durationMs)
    {
        lock (_inputGate)
        {
            try { DragClientPointCore(hwnd, startClientPoint, endClientPoint, durationMs); }
            finally { SendMouse(_mouseDevice, new InterceptionMouseStroke { state = MOUSE_LEFT_UP }); }
        }
    }

    private void DragClientPointCore(nint hwnd, Point startClientPoint, Point endClientPoint, int durationMs)
    {
        if (hwnd == 0) throw new InvalidOperationException("게임 창 핸들이 없습니다.");
        var origin = new NativeMethods.POINT { X = 0, Y = 0 };
        if (!NativeMethods.ClientToScreen(hwnd, ref origin))
            throw new InvalidOperationException("게임 창 좌표를 화면 좌표로 변환하지 못했습니다.");

        int sx = origin.X + startClientPoint.X;
        int sy = origin.Y + startClientPoint.Y;
        int ex = origin.X + endClientPoint.X;
        int ey = origin.Y + endClientPoint.Y;
        NativeMethods.SetForegroundWindow(hwnd);
        Thread.Sleep(100);
        SendAbsolute(_mouseDevice, sx, sy);
        Thread.Sleep(90);
        SendMouse(_mouseDevice, new InterceptionMouseStroke { state = MOUSE_LEFT_DOWN });
        Thread.Sleep(80);

        int steps = Math.Clamp(Math.Max(8, durationMs / 40), 8, 30);
        int delay = Math.Max(15, durationMs / steps);
        for (int i = 1; i <= steps; i++)
        {
            double t = i / (double)steps;
            int x = (int)Math.Round(sx + (ex - sx) * t);
            int y = (int)Math.Round(sy + (ey - sy) * t);
            SendAbsolute(_mouseDevice, x, y);
            Thread.Sleep(delay);
        }

        SendMouse(_mouseDevice, new InterceptionMouseStroke { state = MOUSE_LEFT_UP });
        Thread.Sleep(120);
        if (!GetCursorPos(out var actual) || Math.Abs(actual.X - ex) > 8 || Math.Abs(actual.Y - ey) > 8)
            throw new InvalidOperationException($"Interception 드래그 후 커서 검증 실패: target=({ex},{ey}) cursor=({actual.X},{actual.Y})");
    }

    public void TapScanCode(ushort scanCode)
    {
        lock (_inputGate)
        {
            TapScanCodeCore(scanCode);
        }
    }

    private void TapScanCodeCore(ushort scanCode)
    {
        var down = new InterceptionKeyStroke
        {
            code = scanCode,
            state = 0
        };

        var up = new InterceptionKeyStroke
        {
            code = scanCode,
            state = KEY_UP
        };

        int sentDown = interception_send(
            _context,
            _keyboardDevice,
            ref down,
            1);

        int sentUp;
        try { Thread.Sleep(30); }
        finally { sentUp = interception_send(_context, _keyboardDevice, ref up, 1); }

        if (sentDown <= 0 || sentUp <= 0)
            throw new InvalidOperationException(
                $"Interception keyboard device {_keyboardDevice} 입력 전송 실패");
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

    public void Dispose()
    {
        lock (_inputGate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_context != 0) interception_destroy_context(_context);
        }
    }

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

    public void ClickClientPoint(nint hwnd, Point clientPoint)
    {
        throw new InvalidOperationException(
            "SendInput fallback은 비활성화되어 있습니다. " +
            "게임 클릭은 Interception으로만 전송합니다.");
    }

    public void DragClientPoint(nint hwnd, Point startClientPoint, Point endClientPoint, int durationMs)
    {
        throw new InvalidOperationException(
            "SendInput fallback은 비활성화되어 있습니다. 지도 드래그는 Interception으로만 전송합니다.");
    }

    public void TapScanCode(ushort scanCode)
    {
        throw new InvalidOperationException(
            "SendInput fallback은 비활성화되어 있습니다.");
    }

    public void Dispose() { }
}
