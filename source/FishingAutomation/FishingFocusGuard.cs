using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class FishingFocusGuard
{
    private const int SW_RESTORE = 9;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    public static bool IsForeground(IntPtr target)
        => target != IntPtr.Zero && GetForegroundWindow() == target;

    public static bool Activate()
    {
        IntPtr target = FindGameWindow();
        return Activate(target);
    }

    public static bool Activate(IntPtr target)
    {
        if (target == IntPtr.Zero)
            return false;

        if (IsForeground(target))
            return true;

        if (IsIconic(target))
        {
            ShowWindow(target, SW_RESTORE);
            Thread.Sleep(40);
        }

        IntPtr foreground = GetForegroundWindow();
        uint currentThread = GetCurrentThreadId();
        uint targetThread = GetWindowThreadProcessId(target, out _);
        uint foregroundThread = foreground != IntPtr.Zero
            ? GetWindowThreadProcessId(foreground, out _)
            : 0;

        bool attachedForeground = false;
        bool attachedTarget = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
                attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);

            if (targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread)
                attachedTarget = AttachThreadInput(currentThread, targetThread, true);

            BringWindowToTop(target);
            SetForegroundWindow(target);
            SetActiveWindow(target);
            SetFocus(target);
        }
        finally
        {
            if (attachedTarget)
                AttachThreadInput(currentThread, targetThread, false);
            if (attachedForeground)
                AttachThreadInput(currentThread, foregroundThread, false);
        }

        Thread.Sleep(35);
        if (GetForegroundWindow() != target)
        {
            SetForegroundWindow(target);
            Thread.Sleep(20);
        }
        return GetForegroundWindow() == target;
    }

    private static IntPtr FindGameWindow()
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = -1;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            int length = GetWindowTextLengthW(hWnd);
            if (length <= 0)
                return true;

            var titleBuffer = new StringBuilder(length + 1);
            GetWindowTextW(hWnd, titleBuffer, titleBuffer.Capacity);
            string title = titleBuffer.ToString();
            if (!title.Contains("마비노기 모바일", StringComparison.OrdinalIgnoreCase))
                return true;

            long area = 0;
            if (GetClientRect(hWnd, out var rect))
                area = Math.Max(0, rect.Right - rect.Left) * (long)Math.Max(0, rect.Bottom - rect.Top);

            if (area > bestArea)
            {
                bestArea = area;
                best = hWnd;
            }
            return true;
        }, IntPtr.Zero);

        return best;
    }
}
