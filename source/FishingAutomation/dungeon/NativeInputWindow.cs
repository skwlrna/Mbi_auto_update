using System.Drawing;
using System.Runtime.InteropServices;

namespace DungeonVisionBot;

internal sealed class NativeInputWindow : IInputWindow
{
    internal static readonly NativeInputWindow Instance = new();
    public bool IsIconic(nint hwnd) => NativeMethods.IsIconic(hwnd);
    public bool TryOrigin(nint hwnd, out Point origin)
    {
        var p = new NativeMethods.POINT();
        bool ok = NativeMethods.ClientToScreen(hwnd, ref p);
        origin = new Point(p.X, p.Y);
        return ok;
    }
    public bool HasRequiredSize(nint hwnd)
        => NativeMethods.GetClientRect(hwnd, out var rect) && rect.Right == 800 && rect.Bottom == 1000;
    public bool IsForeground(nint hwnd) => GetForegroundWindow() == hwnd;
    public void Activate(nint hwnd) => NativeMethods.SetForegroundWindow(hwnd);
    public bool TryCursor(out Point cursor)
    {
        bool ok = GetCursorPos(out var p);
        cursor = new Point(p.X, p.Y);
        return ok;
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativeMethods.POINT point);
}
