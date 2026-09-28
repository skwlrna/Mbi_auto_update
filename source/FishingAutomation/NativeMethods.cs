using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FishingAutomation;

internal static class NativeMethods
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int X, int Y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll", SetLastError = true)] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool AdjustWindowRectEx(ref RECT lpRect, uint dwStyle, bool bMenu, uint dwExStyle);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const int SW_RESTORE = 9;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_SCANCODE = 0x0008;
    public const uint INPUT_KEYBOARD = 1;
    public const uint MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;
    public const uint VK_F8 = 0x77;
    public const uint VK_F9 = 0x78;
    public const uint VK_F10 = 0x79;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }
}

public sealed class GameWindow
{
    public IntPtr Handle { get; }
    public int ClientWidth { get; }
    public int ClientHeight { get; }
    public Point ClientScreenOrigin { get; }

    public GameWindow(IntPtr handle, int width, int height, Point origin)
    {
        Handle = handle;
        ClientWidth = width;
        ClientHeight = height;
        ClientScreenOrigin = origin;
    }
}

public static class WindowLocator
{
    public static GameWindow? FindAndPrepare(AutomationConfig cfg, AppLog log)
    {
        IntPtr hwnd = IntPtr.Zero;

        if (!string.IsNullOrWhiteSpace(cfg.GameProcessName))
        {
            foreach (var p in Process.GetProcessesByName(cfg.GameProcessName))
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        hwnd = p.MainWindowHandle;
                        break;
                    }
                }
                catch { }
            }
        }

        if (hwnd == IntPtr.Zero)
        {
            NativeMethods.EnumWindows((h, _) =>
            {
                if (!NativeMethods.IsWindowVisible(h)) return true;
                int len = NativeMethods.GetWindowTextLength(h);
                if (len <= 0) return true;
                var sb = new StringBuilder(len + 1);
                NativeMethods.GetWindowText(h, sb, sb.Capacity);
                string title = sb.ToString();
                if (!string.IsNullOrWhiteSpace(cfg.GameWindowTitleContains) &&
                    title.Contains(cfg.GameWindowTitleContains, StringComparison.OrdinalIgnoreCase))
                {
                    hwnd = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }

        if (hwnd == IntPtr.Zero) return null;

        if (cfg.AutoPlaceWindow)
            TryPlaceWindow(hwnd, cfg, log);

        if (!NativeMethods.GetClientRect(hwnd, out var r)) return null;
        int w = r.Right - r.Left;
        int hgt = r.Bottom - r.Top;
        var pt = new NativeMethods.POINT { X = 0, Y = 0 };
        if (!NativeMethods.ClientToScreen(hwnd, ref pt)) return null;

        if (Math.Abs(w - cfg.ClientWidth) > 2 || Math.Abs(hgt - cfg.ClientHeight) > 2)
        {
            log.Write($"게임 클라이언트 크기 불일치: 현재 {w}x{hgt}, 필요 {cfg.ClientWidth}x{cfg.ClientHeight}");
            return null;
        }

        return new GameWindow(hwnd, w, hgt, new Point(pt.X, pt.Y));
    }

    public static void ActivateForInput(GameWindow window)
    {
        try
        {
            if (NativeMethods.IsIconic(window.Handle))
                NativeMethods.ShowWindow(window.Handle, NativeMethods.SW_RESTORE);
            NativeMethods.BringWindowToTop(window.Handle);
            NativeMethods.SetForegroundWindow(window.Handle);
            Thread.Sleep(45);
        }
        catch { }
    }

    private static void TryPlaceWindow(IntPtr hwnd, AutomationConfig cfg, AppLog log)
    {
        if (!NativeMethods.GetClientRect(hwnd, out var c)) return;
        int cw = c.Right - c.Left;
        int ch = c.Bottom - c.Top;

        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        var outer = new NativeMethods.RECT { Left = 0, Top = 0, Right = cfg.ClientWidth, Bottom = cfg.ClientHeight };
        if (!NativeMethods.AdjustWindowRectEx(ref outer, unchecked((uint)style), false, unchecked((uint)exStyle))) return;

        int ow = outer.Right - outer.Left;
        int oh = outer.Bottom - outer.Top;
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        int x = area.Right - ow;
        int y = area.Top;
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, ow, oh, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        if (cw != cfg.ClientWidth || ch != cfg.ClientHeight)
        {
            Thread.Sleep(250);
            log.Write($"게임 창을 우상단 {cfg.ClientWidth}x{cfg.ClientHeight} 클라이언트 크기로 맞춤 시도");
        }
    }
}
